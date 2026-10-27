using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.Leaderboards;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class LeaderboardGatewayTests
{
    private const string Secret = "leaderboard-http-test-only-secret-at-least-32-characters";

    [Fact]
    public async Task PublicRouteUsesAuthenticatedIdentityForOwnRankAndForbidsCaching()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new Mock<ILeaderboardStore>();
        store.Setup(x => x.GetAsync(It.IsAny<LeaderboardPeriod>(), 1, 20, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardPageDto("week", new(2026, 10, 26), new(2026, 11, 2), "Europe/Minsk", [], 0, 1, 20, null));
        var backendBuilder = Host();
        backendBuilder.Configuration["GatewayAuth:SecretKey"] = Secret;
        backendBuilder.Services.AddHeaderUserContext(backendBuilder.Configuration);
        backendBuilder.Services.AddAuthorization();
        backendBuilder.Services.AddSingleton(store.Object);
        backendBuilder.Services.AddSingleton(TimeProvider.System);
        backendBuilder.Services.AddControllers().AddApplicationPart(typeof(LeaderboardsController).Assembly);
        await using var backend = backendBuilder.Build();
        backend.UseAuthentication(); backend.UseAuthorization(); backend.MapControllers();
        await backend.StartAsync(ct);

        var builder = Host();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-smoke.json"));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GatewayAuth:SecretKey"] = Secret, ["JWTOptions:SecretKey"] = Secret,
            ["JWTOptions:Issuer"] = "leaderboard-test", ["JWTOptions:Audience"] = "leaderboard-test",
            ["JWTOptions:AccessTokenLifetimeMinutes"] = "15", ["JWTOptions:RefreshTokenLifetimeDays"] = "30"
        });
        foreach (var destination in builder.Configuration.GetSection("ReverseProxy:Clusters:account-cluster:Destinations").GetChildren())
            builder.Configuration[$"{destination.Path}:Address"] = backend.Urls.Single();
        var revoked = new Mock<IRevokedUserStore>();
        revoked.Setup(x => x.IsRevokedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        builder.Services.AddSingleton(revoked.Object);
        builder.Services.AddGatewayAuth(builder.Environment, builder.Configuration);
        builder.Services.AddServiceDiscovery();
        builder.Services.AddGatewayRateLimiting().AddGatewayProxy(builder.Configuration);
        await using var gateway = builder.Build();
        gateway.UseAuthentication(); gateway.UseAuthorization(); gateway.UseRateLimiter(); gateway.MapReverseProxy();
        await gateway.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
            const string path = "/api/v1/leaderboard?period=week&date=2026-10-26";
            using var anonymous = await client.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
            Assert.True(anonymous.Headers.CacheControl!.NoStore);
            using var json = JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync(ct));
            Assert.Equal("Europe/Minsk", json.RootElement.GetProperty("data").GetProperty("timeZone").GetString());
            store.Verify(x => x.GetAsync(It.IsAny<LeaderboardPeriod>(), 1, 20, null, It.IsAny<CancellationToken>()), Times.Once);
            var userId = Guid.NewGuid();
            var token = new JwtSecurityToken("leaderboard-test", "leaderboard-test",
                [new Claim("sub", userId.ToString()), new Claim("role", "User")], DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow.AddMinutes(5), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, Guid.NewGuid().ToString());
            using var authenticated = await client.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
            store.Verify(x => x.GetAsync(It.IsAny<LeaderboardPeriod>(), 1, 20, userId, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await gateway.StopAsync(ct); await backend.StopAsync(ct); }
    }

    private static WebApplicationBuilder Host()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }
}
