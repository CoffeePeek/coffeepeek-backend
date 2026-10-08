using Wolverine.Attributes;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

[LocalQueue("shop-profile-cache-invalidation")]
public sealed record ShopProfileCacheInvalidationRequested(Guid ShopId);
