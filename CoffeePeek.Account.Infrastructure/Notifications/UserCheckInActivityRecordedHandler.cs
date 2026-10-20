using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Contract.Events.Shops;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class UserCheckInActivityRecordedHandler(IPushNotificationStore store)
{
    public Task Handle(UserCheckInActivityRecorded message, CancellationToken ct) =>
        store.RecordActivityAsync(message.UserId, message.CreatedAtUtc, true, ct);
}
