using CoffeePeek.Account.Application.Features.Notifications;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class NewShopNotificationBatchHandler(IPushNotificationStore store)
{
    public async Task<NewShopNotificationBatch?> Handle(NewShopNotificationBatch message, CancellationToken ct)
    {
        var after = await store.QueueNewShopBatchAsync(message.ShopId, message.Name, message.PublishedAtUtc, message.AfterUserId, ct);
        return after is null ? null : message with { AfterUserId = after };
    }
}
