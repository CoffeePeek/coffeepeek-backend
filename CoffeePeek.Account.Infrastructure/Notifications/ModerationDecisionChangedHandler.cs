using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Contract.Events.Moderation;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class ModerationDecisionChangedHandler(IPushNotificationStore store, TimeProvider time)
{
    public async Task Handle(ModerationDecisionChanged message, CancellationToken ct)
    {
        var subject = message.Kind switch
        {
            ModerationSubmissionKind.CheckIn => "чекина",
            ModerationSubmissionKind.Shop => "кофейни",
            ModerationSubmissionKind.Roaster => "обжарщика",
            ModerationSubmissionKind.ShopChange => "исправления",
            _ => "жалобы"
        };
        var now = time.GetUtcNow().UtcDateTime;
        var template = NotificationTemplateRenderer.Render(await store.GetTemplateAsync(PushNotificationCategory.Moderation, ct),
            subject, NotificationTemplateRenderer.StatusText(message.Status));
        await store.EnqueueAsync(new PushNotification
        {
            Id = message.EventId, UserId = message.UserId, Category = PushNotificationCategory.Moderation,
            Title = template.Title, Body = template.Body,
            EntityType = message.Kind.ToString(), EntityId = message.SubmissionId, Status = message.Status,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
        }, ct);
    }
}
