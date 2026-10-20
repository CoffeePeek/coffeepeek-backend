using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.Notifications;
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

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class NotificationsGatewayTests
{
    private const string Secret = "notifications-http-test-only-secret-at-least-32-characters";

    [Fact]
    public async Task Gateway_RequiresJwtForDevicesAndAdminRoleForCustomMessagesAndStripsForgedIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new Mock<IPushNotificationStore>();
        store.Setup(x => x.GetPreferencesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new NotificationPreferencesDto());
        var backendBuilder = Host();
        backendBuilder.Configuration["GatewayAuth:SecretKey"] = Secret;
        backendBuilder.Services.AddHeaderUserContext(backendBuilder.Configuration);
        backendBuilder.Services.AddAuthorizationBuilder().AddPolicy("Admin", p => p.RequireRole("Admin"));
        backendBuilder.Services.AddSingleton(store.Object);
        backendBuilder.Services.AddSingleton(TimeProvider.System);
        backendBuilder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
        await using var backend = backendBuilder.Build();
        backend.UseAuthentication(); backend.UseAuthorization(); backend.MapControllers();
        await backend.StartAsync(ct);

        var gatewayBuilder = Host();
        gatewayBuilder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-smoke.json"));
        gatewayBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GatewayAuth:SecretKey"] = Secret, ["JWTOptions:SecretKey"] = Secret,
            ["JWTOptions:Issuer"] = "push-test", ["JWTOptions:Audience"] = "push-test",
            ["JWTOptions:AccessTokenLifetimeMinutes"] = "15", ["JWTOptions:RefreshTokenLifetimeDays"] = "30"
        });
        foreach (var cluster in gatewayBuilder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren())
            foreach (var destination in cluster.GetSection("Destinations").GetChildren())
                gatewayBuilder.Configuration[$"{destination.Path}:Address"] = backend.Urls.Single();
        var revoked = new Mock<IRevokedUserStore>();
        revoked.Setup(x => x.IsRevokedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        gatewayBuilder.Services.AddSingleton(revoked.Object);
        gatewayBuilder.Services.AddGatewayAuth(gatewayBuilder.Environment, gatewayBuilder.Configuration);
        gatewayBuilder.Services.AddServiceDiscovery();
        gatewayBuilder.Services.AddGatewayRateLimiting().AddGatewayProxy(gatewayBuilder.Configuration);
        await using var gateway = gatewayBuilder.Build();
        gateway.UseAuthentication(); gateway.UseAuthorization(); gateway.UseRateLimiter(); gateway.MapReverseProxy();
        await gateway.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
            const string root = "/api/v1/me/notifications";
            using var anonymous = await client.GetAsync(root + "/preferences", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            var userId = Guid.NewGuid();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId, "User"));
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserRole, "Admin");
            var installation = Guid.NewGuid();
            using var registered = await client.PutAsJsonAsync(root + "/devices/" + installation, new { token = "fcm-token" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
            store.Verify(x => x.RegisterDeviceAsync(userId, installation, "fcm-token", It.IsAny<CancellationToken>()), Times.Once);
            using var activity = await client.PostAsync(root + "/activity", null, ct);
            Assert.Equal(HttpStatusCode.NoContent, activity.StatusCode);
            store.Verify(x => x.RecordActivityAsync(userId, It.IsAny<DateTime>(), false, It.IsAny<CancellationToken>()), Times.Once);
            using var preferences = await client.GetAsync(root + "/preferences", ct);
            Assert.Equal(HttpStatusCode.OK, preferences.StatusCode);
            using var missingToken = await client.PutAsJsonAsync(root + "/devices/" + installation, new { }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
            var id = Guid.NewGuid();
            var message = new { userId, title = "Title", body = "Body" };
            using var denied = await client.PutAsJsonAsync("/api/admin/notifications/" + id, message, ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var templatesDenied = await client.GetAsync("/api/admin/notifications/templates", ct);
            Assert.Equal(HttpStatusCode.Forbidden, templatesDenied.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId, "Admin"));
            using var accepted = await client.PutAsJsonAsync("/api/admin/notifications/" + id, message, ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            using var templateSaved = await client.PutAsJsonAsync("/api/admin/notifications/templates/Moderation",
                new { title = "Решение: {status}", body = "Проверка {subject} завершена" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, templateSaved.StatusCode);
            store.Verify(x => x.SetTemplateAsync(It.Is<PushNotificationTemplate>(t => t.Category == PushNotificationCategory.Moderation
                && t.UpdatedByUserId == userId), It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(x => x.EnqueueAsync(It.Is<PushNotification>(n => n.Id == id && n.UserId == userId
                && n.CreatedByUserId == userId && n.Category == PushNotificationCategory.Custom), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await gateway.StopAsync(ct); await backend.StopAsync(ct); }
    }

    private static WebApplicationBuilder Host()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static string Token(Guid userId, string role) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("push-test", "push-test", [new Claim("sub", userId.ToString()), new Claim("role", role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10), new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)));
}
