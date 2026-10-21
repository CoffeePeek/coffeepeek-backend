namespace CoffeePeek.Account.Application.Features.Notifications;

public sealed record NewShopNotificationBatch(Guid ShopId, string Name, DateTime PublishedAtUtc, Guid? AfterUserId);
