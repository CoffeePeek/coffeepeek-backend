using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace CoffeePeek.Gateway.Tests;

public sealed class AppReleaseAutomationGatewayHttpTests
{
    private const string Secret = "test-only-release-gateway-secret-at-least-32-characters";
    private const string AutomationToken = "test-only-app-release-automation-token-at-least-32-characters";
    private const string ActorId = "11111111-2222-4333-8444-555555555555";
    private const string AdminId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private const string ReleasePath = "/api/admin/v1/app-downloads/android/releases/00000000-0000-0000-0000-000000000123";

    [Fact]
    public async Task AutomationPublishForwardsOnlyItsConfiguredTrustedActor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(ct);
        using var request = Request(HttpMethod.Post, ReleasePath + "/publish", AutomationToken);
        request.Headers.Add(GatewayHeaderConsts.XUserId, AdminId);
        request.Headers.Add(GatewayHeaderConsts.XUserRole, RoleConsts.User);
        request.Headers.Add(GatewayHeaderConsts.XGatewayAuth, "forged-test-header");
        request.Headers.Add(GatewayHeaderConsts.XUserTokenExpiresAt, "253402300799");
        using var response = await fixture.Client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(ActorId, body.RootElement.GetProperty("actorId").GetString());
        Assert.Equal(RoleConsts.Admin, body.RootElement.GetProperty("roles").GetString());
        Assert.Equal("", body.RootElement.GetProperty("expiry").GetString());
        Assert.Equal(ReleasePath + "/publish", body.RootElement.GetProperty("path").GetString());
        Assert.Equal("POST", body.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, fixture.DownstreamCalls);
        Assert.Equal(0, fixture.Revocations.Lookups);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("bad-token", HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("expired-admin", HttpStatusCode.Unauthorized)]
    [InlineData("revoked-admin", HttpStatusCode.Unauthorized)]
    public async Task PublishRejectsUntrustedCredentialsBeforeReachingShops(string credential, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(ct);
        fixture.Revocations.Revoked = credential == "revoked-admin";
        var token = credential switch
        {
            "anonymous" => null,
            "bad-token" => "test-only-invalid-automation-token",
            "user" => Token(RoleConsts.User),
            "expired-admin" => Token(RoleConsts.Admin, expired: true),
            _ => Token(RoleConsts.Admin)
        };
        using var request = Request(HttpMethod.Post, ReleasePath + "/publish", token);
        request.Headers.Add(GatewayHeaderConsts.XUserId, AdminId);
        request.Headers.Add(GatewayHeaderConsts.XUserRole, RoleConsts.Admin);
        request.Headers.Add(GatewayHeaderConsts.XGatewayAuth, Secret);
        using var response = await fixture.Client.SendAsync(request, ct);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    [Theory]
    [InlineData("GET", ReleasePath + "/publish")]
    [InlineData("HEAD", ReleasePath + "/publish")]
    [InlineData("DELETE", ReleasePath + "/publish")]
    [InlineData("PUT", ReleasePath + "/publish")]
    [InlineData("PATCH", ReleasePath + "/publish")]
    [InlineData("POST", "/api/admin/v1/app-downloads/android/releases/not-a-guid/publish")]
    [InlineData("POST", ReleasePath + "/publish/other")]
    [InlineData("PUT", "/api/admin/v1/app-downloads")]
    [InlineData("DELETE", ReleasePath)]
    [InlineData("POST", "/api/admin/shops/00000000-0000-0000-0000-000000000123")]
    public async Task AutomationTokenCannotUseOtherPublishMethodsPathsOrAdminWrites(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(ct);
        using var request = Request(new HttpMethod(method), path, AutomationToken);
        using var response = await fixture.Client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    [Fact]
    public async Task AdminJwtRetainsPublishAndExistingAdminAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(ct);
        foreach (var (method, path) in new[]
                 { (HttpMethod.Post, ReleasePath + "/publish"), (HttpMethod.Delete, ReleasePath) })
        {
            using var request = Request(method, path, Token(RoleConsts.Admin));
            using var response = await fixture.Client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            Assert.Equal(AdminId, body.RootElement.GetProperty("actorId").GetString());
            Assert.Equal(RoleConsts.Admin, body.RootElement.GetProperty("roles").GetString());
        }
        Assert.Equal(2, fixture.DownstreamCalls);
        Assert.Equal(2, fixture.Revocations.Lookups);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static string Token(string role, bool expired = false) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("release-test", "release-test",
            [new Claim("sub", AdminId), new Claim(ClaimTypes.Role, role)],
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)));

    private sealed class Fixture(WebApplication downstream, WebApplication gateway, Revocations revocations) : IAsyncDisposable
    {
        private int _downstreamCalls;
        public HttpClient Client { get; } = new() { BaseAddress = new Uri(gateway.Urls.Single()) };
        public Revocations Revocations { get; } = revocations;
        public int DownstreamCalls => Volatile.Read(ref _downstreamCalls);

        public static async Task<Fixture> StartAsync(CancellationToken ct)
        {
            var downstreamBuilder = WebApplication.CreateBuilder();
            downstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            downstreamBuilder.Logging.ClearProviders();
            downstreamBuilder.Services.Configure<GatewayAuthOptions>(options => options.SecretKey = Secret);
            downstreamBuilder.Services.AddAuthentication("Header")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Header", null);
            downstreamBuilder.Services.AddAuthorizationBuilder().AddPolicy(RoleConsts.Admin, policy => policy.RequireRole(RoleConsts.Admin));
            var downstream = downstreamBuilder.Build();
            downstream.UseAuthentication();
            downstream.UseAuthorization();
            Fixture? fixture = null;
            downstream.Map("/{**remainder}", (HttpContext context) =>
            {
                Interlocked.Increment(ref fixture!._downstreamCalls);
                return Results.Json(new
                {
                    actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    roles = context.Request.Headers[GatewayHeaderConsts.XUserRole].ToString(),
                    expiry = context.Request.Headers[GatewayHeaderConsts.XUserTokenExpiresAt].ToString(),
                    path = context.Request.Path.Value,
                    method = context.Request.Method
                });
            }).RequireAuthorization(RoleConsts.Admin);
            await downstream.StartAsync(ct);
            WebApplication? gateway = null;
            try
            {
                var gatewayBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                gatewayBuilder.WebHost.UseUrls("http://127.0.0.1:0");
                gatewayBuilder.Logging.ClearProviders();
                gatewayBuilder.Configuration.AddJsonFile(Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "CoffeePeek.Gateway", "appsettings.json")));
                var overrides = new Dictionary<string, string?>
                {
                    ["JWTOptions:SecretKey"] = Secret, ["JWTOptions:Issuer"] = "release-test", ["JWTOptions:Audience"] = "release-test",
                    ["JWTOptions:AccessTokenLifetimeMinutes"] = "15", ["JWTOptions:RefreshTokenLifetimeDays"] = "30",
                    ["GatewayAuth:SecretKey"] = Secret, ["AppReleaseAutomation:Token"] = AutomationToken,
                    ["AppReleaseAutomation:ActorUserId"] = ActorId
                };
                foreach (var cluster in gatewayBuilder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren())
                {
                    overrides[$"ReverseProxy:Clusters:{cluster.Key}:HealthCheck:Active:Enabled"] = "false";
                    overrides[$"ReverseProxy:Clusters:{cluster.Key}:HealthCheck:Passive:Enabled"] = "false";
                    foreach (var destination in cluster.GetSection("Destinations").GetChildren())
                        overrides[$"ReverseProxy:Clusters:{cluster.Key}:Destinations:{destination.Key}:Address"] = downstream.Urls.Single();
                }
                gatewayBuilder.Configuration.AddInMemoryCollection(overrides);
                var revocations = new Revocations();
                gatewayBuilder.Services.AddSingleton<IRevokedUserStore>(revocations);
                gatewayBuilder.Services.AddGatewayAuth(gatewayBuilder.Environment, gatewayBuilder.Configuration);
                gatewayBuilder.Services.AddGatewayRateLimiting();
                gatewayBuilder.Services.AddReverseProxy().LoadFromConfig(gatewayBuilder.Configuration.GetSection("ReverseProxy"))
                    .AddTransforms<ClaimsToHeadersTransformProvider>();
                gateway = gatewayBuilder.Build();
                gateway.UseRateLimiter();
                gateway.UseAuthentication();
                gateway.UseAuthorization();
                gateway.MapReverseProxy();
                await gateway.StartAsync(ct);
                fixture = new Fixture(downstream, gateway, revocations);
                return fixture;
            }
            catch
            {
                if (gateway is not null) await gateway.DisposeAsync();
                await downstream.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await gateway.DisposeAsync();
            await downstream.DisposeAsync();
        }
    }

    private sealed class Revocations : IRevokedUserStore
    {
        public bool Revoked;
        public int Lookups;
        public Task<bool> IsRevokedAsync(Guid userId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Lookups);
            return Task.FromResult(Revoked);
        }
        public Task<bool> RevokeAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
