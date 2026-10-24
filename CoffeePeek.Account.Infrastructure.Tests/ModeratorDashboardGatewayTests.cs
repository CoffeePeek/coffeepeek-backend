using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CoffeePeek.Account.Application.Features.Admin.Users;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class ModeratorDashboardGatewayTests
{
    private const string Secret = "dashboard-http-test-only-secret-at-least-32-characters";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DashboardAndUserList_AllowModeratorsAndAdmins_KeepUserManagementAdminOnly()
    {
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        ReadResponse<AdminOverviewStatsDto>(bus);
        ReadResponse<AdminUsersTimeseriesDto>(bus);
        ReadResponse<AdminShopsTimeseriesDto>(bus);
        ReadResponse<AdminShopsInsightsDto>(bus);
        ReadResponse<AdminModerationInsightsDto>(bus);
        ReadResponse<AdminServiceStatsDto>(bus);
        ReadResponse<GetAdminUsersStatsResponse>(bus);
        bus.Setup(x => x.InvokeAsync<Response<GetAdminUsersResponse>>(
                It.IsAny<GetAdminUsersQuery>(), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Response<GetAdminUsersResponse>.Success(new([], 0, 0, 2, 10)));

        await using var account = Service(typeof(AdminUserQueriesController), bus.Object);
        await using var shops = Service(typeof(CoffeePeek.ShopsService.Controllers.AdminStatsController), bus.Object);
        await using var moderation = Service(typeof(CoffeePeek.ModerationService.Controllers.AdminStatsController), bus.Object);
        await account.StartAsync(Ct);
        await shops.StartAsync(Ct);
        await moderation.StartAsync(Ct);

        var builder = Host();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-smoke.json"));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GatewayAuth:SecretKey"] = Secret,
            ["JWTOptions:SecretKey"] = Secret,
            ["JWTOptions:Issuer"] = "dashboard-test",
            ["JWTOptions:Audience"] = "dashboard-test",
            ["JWTOptions:AccessTokenLifetimeMinutes"] = "15",
            ["JWTOptions:RefreshTokenLifetimeDays"] = "30"
        });
        foreach (var (cluster, service) in new[]
                 { ("account-cluster", account), ("shops-cluster", shops), ("moderation-cluster", moderation) })
        {
            foreach (var destination in builder.Configuration.GetSection($"ReverseProxy:Clusters:{cluster}:Destinations").GetChildren())
                builder.Configuration[$"{destination.Path}:Address"] = service.Urls.Single();
        }
        var revoked = new Mock<IRevokedUserStore>();
        revoked.Setup(x => x.IsRevokedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        builder.Services.AddSingleton(revoked.Object);
        builder.Services.AddGatewayAuth(builder.Environment, builder.Configuration);
        builder.Services.AddServiceDiscovery();
        builder.Services.AddGatewayRateLimiting().AddGatewayProxy(builder.Configuration);
        await using var gateway = builder.Build();
        gateway.UseAuthentication();
        gateway.UseAuthorization();
        gateway.UseRateLimiter();
        gateway.MapReverseProxy();
        await gateway.StartAsync(Ct);

        using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        string[] reads =
        [
            "/api/admin/stats/overview", "/api/admin/stats/users/timeseries?days=7",
            "/api/admin/stats/shops/insights", "/api/admin/stats/shops/timeseries?days=7",
            "/api/admin/stats/moderation/insights", "/api/admin/users/stats",
            "/api/admin/users?page=2&pageSize=10&search=test&role=Moderator"
        ];
        foreach (var (role, expected) in new (string?, HttpStatusCode)[]
                 {
                     (null, HttpStatusCode.Unauthorized), (RoleConsts.User, HttpStatusCode.Forbidden),
                     (RoleConsts.Owner, HttpStatusCode.Forbidden),
                     (RoleConsts.Moderator, HttpStatusCode.OK), (RoleConsts.Admin, HttpStatusCode.OK)
                 })
        {
            client.DefaultRequestHeaders.Authorization = role is null ? null : new("Bearer", Token(role));
            foreach (var path in reads)
            {
                using var response = await client.GetAsync(path, Ct);
                Assert.Equal(expected, response.StatusCode);
                if (expected == HttpStatusCode.OK && path.StartsWith("/api/admin/users?"))
                {
                    Assert.Equal("2", response.Headers.GetValues("X-Current-Page").Single());
                    Assert.Equal("10", response.Headers.GetValues("X-Page-Size").Single());
                }
            }
        }
        bus.Verify(x => x.InvokeAsync<Response<GetAdminUsersResponse>>(
            It.Is<GetAdminUsersQuery>(q => q.Page == 2 && q.PageSize == 10 && q.Search == "test" && q.Role == RoleConsts.Moderator),
            It.IsAny<CancellationToken>(), null), Times.Exactly(2));

        var userId = Guid.NewGuid();
        (HttpMethod Method, string Path)[] restricted =
        [
            (HttpMethod.Patch, $"/api/admin/users/{userId}/role"),
            (HttpMethod.Patch, $"/api/admin/users/{userId}/block"),
            (HttpMethod.Delete, $"/api/admin/users/{userId}"),
            (HttpMethod.Get, $"/api/admin/users/{userId}/sessions"),
            (HttpMethod.Delete, $"/api/admin/users/{userId}/sessions"),
            (HttpMethod.Delete, $"/api/admin/users/{userId}/sessions/{Guid.NewGuid()}")
        ];
        // Check both authorization boundaries, including direct trusted-header requests to Account.
        using var directAccount = TrustedClient(account, RoleConsts.Moderator);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(RoleConsts.Moderator));
        foreach (var target in new[] { client, directAccount })
        {
            foreach (var (method, path) in restricted)
            {
                using var request = new HttpRequestMessage(method, path);
                using var response = await target.SendAsync(request, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        // Overview aggregation forwards the caller's role to these internal summary endpoints.
        foreach (var service in new[] { shops, moderation })
        {
            foreach (var role in new[] { RoleConsts.Moderator, RoleConsts.Admin, RoleConsts.User })
            {
                using var direct = TrustedClient(service, role);
                using var summary = await direct.GetAsync("/api/admin/stats/summary", Ct);
                Assert.Equal(role == RoleConsts.User ? HttpStatusCode.Forbidden : HttpStatusCode.OK, summary.StatusCode);
            }
        }
    }

    private static void ReadResponse<T>(Mock<IMessageBus> bus) =>
        bus.Setup(x => x.InvokeAsync<Response<T>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Response<T>.Success(default!));

    private static WebApplication Service(Type controller, IMessageBus bus)
    {
        var builder = Host();
        builder.Configuration["GatewayAuth:SecretKey"] = Secret;
        builder.Services.AddHeaderUserContext(builder.Configuration);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RoleConsts.Admin, p => p.RequireRole(RoleConsts.Admin))
            .AddPolicy(RoleConsts.Moderator, p => p.RequireRole(RoleConsts.Moderator, RoleConsts.Admin));
        builder.Services.AddSingleton(bus);
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.ApplicationParts.Add(new AssemblyPart(controller.Assembly));
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }

    private static WebApplicationBuilder Host()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static HttpClient TrustedClient(WebApplication service, string role)
    {
        var client = new HttpClient { BaseAddress = new Uri(service.Urls.Single()) };
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, Secret);
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserRole, role);
        return client;
    }

    private static string Token(string role) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("dashboard-test", "dashboard-test",
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)));
}
