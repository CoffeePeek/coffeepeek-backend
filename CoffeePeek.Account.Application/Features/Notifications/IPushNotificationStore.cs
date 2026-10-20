using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Contract.Dtos.Notifications;

namespace CoffeePeek.Account.Application.Features.Notifications;

public interface IPushNotificationStore
{
    Task RegisterDeviceAsync(Guid userId, Guid installationId, string token, CancellationToken ct);
    Task RemoveDeviceAsync(Guid userId, Guid installationId, CancellationToken ct);
    Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct);
    Task SetPreferencesAsync(Guid userId, NotificationPreferencesDto preferences, CancellationToken ct);
    Task RecordActivityAsync(Guid userId, DateTime atUtc, bool checkIn, CancellationToken ct);
    Task EnqueueAsync(PushNotification notification, CancellationToken ct);
    Task QueueRemindersAsync(int inactiveDays, int repeatDays, CancellationToken ct);
    Task<PushDispatch?> ClaimAsync(CancellationToken ct);
    Task CompleteAsync(PushDispatch dispatch, PushSendResult result, CancellationToken ct);
    Task PurgeAsync(CancellationToken ct);
    Task<PushNotificationTemplate> GetTemplateAsync(PushNotificationCategory category, CancellationToken ct);
    Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(CancellationToken ct);
    Task SetTemplateAsync(PushNotificationTemplate template, CancellationToken ct);
}
