using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shared.Persistence.Cache;

public sealed class CacheInvalidationStrategy(ICacheService cacheService, ILogger<CacheInvalidationStrategy> logger) 
    : ICacheInvalidationStrategy
{
    public static class Tags
    {
        public const string ShopsDictionary = "shops:dictionary";
        public const string ShopsLists = "shops:lists";
        public const string ShopsDetails = "shops:details";
    }

    // Tag -> redis patterns
    private static readonly Dictionary<string, string[]> TagPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            Tags.ShopsDictionary,
            [
                "city:list:*",
                "equipment:list:*",
                "coffeebean:list:*",
                "roaster:list:*",
                "brewmethod:list:*"
            ]
        },
        {
            Tags.ShopsLists,
            [
                "shop:list:city:*" // all paged lists by city
            ]
        },
        {
            Tags.ShopsDetails,
            [
                "shop:detail:*"
            ]
        }
    };

    public async Task InvalidateAsync(CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        await cacheService.RemoveAsync(cacheKey);
    }

    public async Task InvalidateTagsAsync(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!TagPatterns.TryGetValue(tag, out var patterns))
            {
                logger.LogDebug("No cache patterns registered for tag {Tag}", tag);
                continue;
            }

            foreach (var pattern in patterns)
            {
                await cacheService.RemoveByPattern(pattern);
            }
        }
    }
}

