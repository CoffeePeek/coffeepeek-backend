using Wolverine.Attributes;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

[LocalQueue("checkin-cache-invalidation")]
public sealed record CheckInCacheInvalidationRequested(
    Guid CheckInId,
    Guid? ShopId = null,
    Guid? UserId = null);
