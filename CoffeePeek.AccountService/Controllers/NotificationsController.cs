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
