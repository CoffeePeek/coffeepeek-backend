using CoffeePeek.Contract.Dtos.Public;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Public.Stats;

public static class PublicStatsCacheInvalidator
{
    public static Task InvalidateAsync(ICacheService cacheService, CancellationToken ct = default) =>
        cacheService.RemoveAsync(CacheKey.Shop.PublicPlatformStats());
}
