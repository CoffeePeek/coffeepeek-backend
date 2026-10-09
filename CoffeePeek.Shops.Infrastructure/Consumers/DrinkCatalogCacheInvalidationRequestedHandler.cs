using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public sealed class DrinkCatalogCacheInvalidationRequestedHandler
{
    public static void Configure(HandlerChain chain)
    {
        chain.OnException<RedisException>().ScheduleRetry(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    }

    public static async Task Handle(
        DrinkCatalogCacheInvalidationRequested message,
        IStrictCacheInvalidator cache,
        ILogger<DrinkCatalogCacheInvalidationRequestedHandler> logger,
        CancellationToken ct)
    {
        try
        {
            // Redelivery is safe: invalidation only removes keys after the catalog commit.
            await cache.RemoveAsync(CacheKey.Shop.MenuDrinks(), ct);
            await cache.RemoveByPatternAsync(CacheKey.Shop.DetailPattern(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Drink catalog cache invalidation failed; Wolverine will retry");
            throw;
        }
    }
}
