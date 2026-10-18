using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Gateway.Middleware;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;

namespace CoffeePeek.Gateway.Tests;

public sealed class CheckInPhotoGatewayHttpTests
{
    private const string Secret = "photo-gateway-test-only-secret-at-least-32-characters";
    private const string Path = "/api/v1/check-ins/00000000-0000-0000-0000-000000000001/photos/00000000-0000-0000-0000-000000000002/variants/source/1/feed.webp";

    [Fact]
    public async Task RealJwtAndYarpRejectBadCredentialsBeforeAnonymousCacheAndStripForgedExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var downstreamBuilder = WebApplication.CreateBuilder();
        downstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        downstreamBuilder.Services.Configure<GatewayAuthOptions>(o => o.SecretKey = Secret);
        downstreamBuilder.Services.AddAuthentication("Header")
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Header", null);
        await using var downstream = downstreamBuilder.Build();
        downstream.UseAuthentication();
        var calls = 0;
        var representationDate = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7);
        downstream.MapMethods(Path, ["GET", "HEAD"], (HttpContext context) =>
        {
            calls++;
            context.Response.Headers.CacheControl = "private, max-age=20, must-revalidate";
            context.Response.GetTypedHeaders().Date = representationDate;
            context.Response.Headers.Age = "7";
            context.Response.Headers.Vary = "Authorization, Cookie";
            context.Response.Headers.ETag = "\"test\"";
            var expiry = AccessTokenExpiry.Read(context.User);
            return context.Request.Headers.IfNoneMatch == "\"test\""
                ? Results.StatusCode(304) : Results.Text(expiry?.ToUnixTimeSeconds().ToString() ?? "anonymous");
        }).AllowAnonymous();
        await downstream.StartAsync(ct);

        var gatewayBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        gatewayBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        gatewayBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWTOptions:SecretKey"] = Secret, ["JWTOptions:Issuer"] = "photo-test", ["JWTOptions:Audience"] = "photo-test",
            ["JWTOptions:AccessTokenLifetimeMinutes"] = "15", ["JWTOptions:RefreshTokenLifetimeDays"] = "30",
            ["GatewayAuth:SecretKey"] = Secret
        });
        var revocation = new Revocations();
        gatewayBuilder.Services.AddSingleton<IRevokedUserStore>(revocation);
        gatewayBuilder.Services.AddGatewayAuth(gatewayBuilder.Environment, gatewayBuilder.Configuration);
        gatewayBuilder.Services.AddCorsModule(gatewayBuilder.Environment);
        gatewayBuilder.Services.AddResponseCaching();
        gatewayBuilder.Services.AddReverseProxy().LoadFromMemory(
            [new RouteConfig { RouteId = "photos", ClusterId = "shops", Match = new RouteMatch { Path = "/api/v1/check-ins/{**remainder}" }, AuthorizationPolicy = "anonymous" }],
            [new ClusterConfig { ClusterId = "shops", Destinations = new Dictionary<string, DestinationConfig>
                { ["local"] = new() { Address = downstream.Urls.Single() } } }])
            .AddTransforms<ClaimsToHeadersTransformProvider>();
        await using var gateway = gatewayBuilder.Build();
        gateway.UseCors(); gateway.UseAuthentication(); gateway.UseMiddleware<CheckInPhotoAuthenticationMiddleware>();
        gateway.UseAuthorization(); gateway.UseResponseCaching(); gateway.MapReverseProxy();
        await gateway.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
            const string origin = "http://localhost:3000";
            client.DefaultRequestHeaders.Add("Origin", origin);
            using (var preflight = new HttpRequestMessage(HttpMethod.Options, Path))
            {
                preflight.Headers.Add("Access-Control-Request-Method", "GET");
                preflight.Headers.Add("Access-Control-Request-Headers", "authorization,if-none-match,cache-control");
                using var response = await client.SendAsync(preflight, ct);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
                var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single();
                Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("if-none-match", allowedHeaders, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("cache-control", allowedHeaders, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, calls);
            }
            for (var i = 0; i < 2; i++)
            {
                using var anonymous = new HttpRequestMessage(HttpMethod.Get, Path);
                anonymous.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, Guid.NewGuid().ToString());
                anonymous.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserTokenExpiresAt, "253402300799");
                using var response = await client.SendAsync(anonymous, ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("anonymous", await response.Content.ReadAsStringAsync(ct));
                Assert.Equal(representationDate, response.Headers.Date);
                Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.Age);
                AssertBrowserCanReadFreshness(response, origin);
            }
            Assert.Equal(2, calls); // Private responses never enter Gateway's shared response cache.

            foreach (var token in new[] { "malformed", Token(DateTime.UtcNow.AddMinutes(-1)), Token(DateTime.UtcNow.AddMinutes(1), "wrong-test-signing-key-at-least-32-characters") })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Path);
                request.Headers.Authorization = new("Bearer", token); request.Headers.IfNoneMatch.ParseAdd("\"test\"");
                using var denied = await client.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                Assert.True(denied.Headers.CacheControl!.NoStore);
                Assert.Null(denied.Headers.ETag);
            }
            Assert.Equal(2, calls);

            var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
            using var valid = new HttpRequestMessage(HttpMethod.Get, Path);
            valid.Headers.Authorization = new("Bearer", Token(DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime));
            valid.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserTokenExpiresAt, "253402300799");
            using var accepted = await client.SendAsync(valid, ct);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(expiry.ToString(), await accepted.Content.ReadAsStringAsync(ct));
            Assert.Equal(3, calls);

            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                using var conditional = new HttpRequestMessage(method, Path);
                conditional.Headers.Authorization = valid.Headers.Authorization;
                conditional.Headers.IfNoneMatch.ParseAdd("\"test\"");
                using var unchanged = await client.SendAsync(conditional, ct);
                Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
                Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync(ct));
                Assert.Equal(representationDate, unchanged.Headers.Date);
                Assert.Equal(TimeSpan.FromSeconds(7), unchanged.Headers.Age);
                Assert.True(unchanged.Headers.CacheControl!.Private);
                Assert.Equal(TimeSpan.FromSeconds(20), unchanged.Headers.CacheControl.MaxAge);
                AssertBrowserCanReadFreshness(unchanged, origin);
            }
            Assert.Equal(5, calls);

            revocation.Revoked = true;
            using var revoked = new HttpRequestMessage(HttpMethod.Get, Path);
            revoked.Headers.Authorization = valid.Headers.Authorization;
            using var rejected = await client.SendAsync(revoked, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(5, calls);
        }
        finally { await gateway.StopAsync(ct); await downstream.StopAsync(ct); }
    }

    private static void AssertBrowserCanReadFreshness(HttpResponseMessage response, string origin)
    {
        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        var exposed = response.Headers.GetValues("Access-Control-Expose-Headers")
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries)).ToArray();
        foreach (var header in new[] { "ETag", "Date", "Age" })
            Assert.Contains(exposed, value => string.Equals(value, header, StringComparison.OrdinalIgnoreCase));
    }

    private static string Token(DateTime expiry, string secret = Secret) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("photo-test", "photo-test", [new Claim("sub", Guid.NewGuid().ToString())],
            DateTime.UtcNow.AddHours(-1), expiry, new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));

    private sealed class Revocations : IRevokedUserStore
    {
        public bool Revoked;
        public Task<bool> IsRevokedAsync(Guid userId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default) => Task.FromResult(Revoked);
        public Task<bool> RevokeAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
