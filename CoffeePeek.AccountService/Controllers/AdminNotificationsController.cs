using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Authorize(Policy = RoleConsts.Admin)]
[Route("api/admin/notifications")]
public sealed class AdminNotificationsController(IPushNotificationStore store, IUserContext user, TimeProvider time) : ControllerBase
{
    [HttpGet("templates")]
    public async Task<IActionResult> Templates(CancellationToken ct) => Ok(await store.GetTemplatesAsync(ct));

    [HttpPut("templates/{category}")]
    public async Task<IActionResult> UpdateTemplate(string category, UpdateNotificationTemplateRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<PushNotificationCategory>(category, true, out var parsed)
            || parsed is not (PushNotificationCategory.Moderation or PushNotificationCategory.Inactivity or PushNotificationCategory.NewShop))
            throw new ValidationException("Unknown automatic notification template.");
        await store.SetTemplateAsync(new PushNotificationTemplate
        {
            Category = parsed, Title = request.Title, Body = request.Body, UpdatedByUserId = user.GetUserIdOrThrow()
        }, ct);
        return NoContent();
    }

    /// <summary>Queue a custom push. Reuse the notification ID when retrying an ambiguous HTTP request.</summary>
    [HttpPut("{notificationId:guid}")]
    public async Task<IActionResult> Send(Guid notificationId, SendPushNotificationRequest request, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty) throw new ValidationException("UserId is required.");
        var now = time.GetUtcNow().UtcDateTime;
        await store.EnqueueAsync(new PushNotification
        {
            Id = notificationId, UserId = request.UserId, CreatedByUserId = user.GetUserIdOrThrow(),
            Category = PushNotificationCategory.Custom, Title = request.Title.Trim(), Body = request.Body.Trim(),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(1)
        }, ct);
        return Accepted(new { notificationId });
    }
}
