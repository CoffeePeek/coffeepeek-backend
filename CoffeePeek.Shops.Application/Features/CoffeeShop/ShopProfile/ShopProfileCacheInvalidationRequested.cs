using Wolverine.Attributes;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

[LocalQueue("shop-profile-cache-invalidation")]
public sealed record ShopProfileCacheInvalidationRequested(Guid ShopId)
{
    // Import enrichment can change many shops in one transaction. Keep one
    // global search/list invalidation per batch while deleting each detail key.
    public IReadOnlyList<Guid> AdditionalShopIds { get; init; } = [];
}
