using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Public.Stats;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CheckInCacheInvalidator
{
    public static async Task InvalidateAsync(ICacheService cache, Guid shopId, Guid userId, CancellationToken ct = default)
    {
        await cache.RemoveAsync(CacheKey.Shop.Detail(shopId));
        await cache.RemoveAsync(CacheKey.Shop.Visited(userId));
        await cache.RemoveAsync(CacheKey.Shop.VisitStatistics(userId));
        await cache.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        await cache.RemoveByPattern(CacheKey.Shop.ListPattern(), ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cache, ct);
    }
}
