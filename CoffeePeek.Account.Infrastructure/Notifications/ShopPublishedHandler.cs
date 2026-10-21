using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Contract.Events.Shops;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public static class ShopPublishedHandler
{
    public static NewShopNotificationBatch Handle(ShopPublished message) =>
        new(message.ShopId, message.Name, message.PublishedAtUtc, null);
}
