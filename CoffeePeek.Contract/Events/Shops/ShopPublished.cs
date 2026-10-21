namespace CoffeePeek.Contract.Events.Shops;

public sealed record ShopPublished(Guid ShopId, string Name, DateTime PublishedAtUtc);
