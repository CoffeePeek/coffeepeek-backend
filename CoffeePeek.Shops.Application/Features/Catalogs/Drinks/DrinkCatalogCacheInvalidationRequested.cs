using Wolverine.Attributes;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

[LocalQueue("drink-catalog-cache-invalidation")]
public sealed record DrinkCatalogCacheInvalidationRequested;
