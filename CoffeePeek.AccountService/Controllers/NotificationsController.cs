using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(IPushNotificationStore store, IUserContext user, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Inbox([FromQuery] Guid? before, CancellationToken ct, [FromQuery] int pageSize = 30) =>
        Ok(await store.GetInboxAsync(user.GetUserIdOrThrow(), before, pageSize, ct));

    [HttpPut("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken ct)
    {
        await store.MarkReadAsync(user.GetUserIdOrThrow(), notificationId, ct);
        return NoContent();
    }

    [HttpPut("read")]
    public async Task<IActionResult> MarkAllRead(ReadNotificationsRequest request, CancellationToken ct)
    {
        await store.MarkAllReadAsync(user.GetUserIdOrThrow(), request.ThroughId, ct);
        return NoContent();
    }

    [HttpGet("push")]
    public async Task<IActionResult> PushSettings(CancellationToken ct) =>
        Ok(await store.GetPushSettingsAsync(user.GetUserIdOrThrow(), ct));

    [HttpPut("push")]
    public async Task<IActionResult> SetPushSettings(PushSettingsDto request, CancellationToken ct)
    {
        await store.SetPushEnabledAsync(user.GetUserIdOrThrow(), request.Enabled, ct);
        return NoContent();
    }

    [HttpPut("devices/{installationId:guid}")]
    public async Task<IActionResult> RegisterDevice(Guid installationId, RegisterPushDeviceRequest request, CancellationToken ct)
    {
        await store.RegisterDeviceAsync(user.GetUserIdOrThrow(), installationId, request.Token, ct);
        return NoContent();
    }

    [HttpDelete("devices/{installationId:guid}")]
    public async Task<IActionResult> RemoveDevice(Guid installationId, CancellationToken ct)
    {
        await store.RemoveDeviceAsync(user.GetUserIdOrThrow(), installationId, ct);
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> Preferences(CancellationToken ct) =>
        Ok(await store.GetPreferencesAsync(user.GetUserIdOrThrow(), ct));

    [HttpPut("preferences")]
    public async Task<IActionResult> SetPreferences(NotificationPreferencesDto preferences, CancellationToken ct)
    {
        await store.SetPreferencesAsync(user.GetUserIdOrThrow(), preferences, ct);
        return NoContent();
    }

    [HttpPost("activity")]
    public async Task<IActionResult> Activity(CancellationToken ct)
    {
        await store.RecordActivityAsync(user.GetUserIdOrThrow(), time.GetUtcNow().UtcDateTime, false, ct);
        return NoContent();
    }
}
