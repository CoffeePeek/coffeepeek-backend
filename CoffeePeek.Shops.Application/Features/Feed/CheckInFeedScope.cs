namespace CoffeePeek.Shops.Application.Features.Feed;

public sealed record CheckInFeedScope(Guid? CityId = null, Guid? ShopId = null, Guid? AuthorId = null);
