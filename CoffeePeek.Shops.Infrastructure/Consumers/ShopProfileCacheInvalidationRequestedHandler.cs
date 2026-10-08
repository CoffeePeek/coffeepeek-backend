using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Observability;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public sealed class ShopProfileCacheInvalidationRequestedHandler
{
    public static void Configure(HandlerChain chain)
    {
        chain.OnException<RedisException>().ScheduleRetry(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    }

    public static async Task Handle(
        ShopProfileCacheInvalidationRequested message,
        IStrictCacheInvalidator cache,
        ILogger<ShopProfileCacheInvalidationRequestedHandler> logger,
        CancellationToken ct)
    {
        using var activity = ShopsTelemetry.ActivitySource.StartActivity("shops.cache.invalidate.profile");
        activity?.SetTag("shop.id", message.ShopId);
        try
        {
            // Removing an already-absent key is safe when the durable queue redelivers.
            await cache.RemoveAsync(CacheKey.Shop.Detail(message.ShopId), ct);
            await cache.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(), ct);
            await cache.RemoveByPatternAsync(CacheKey.Shop.ListPattern(), ct);
            await cache.RemoveAsync(CacheKey.Shop.PublicPlatformStats(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShopsTelemetry.ProfileCacheInvalidationFailed();
            logger.LogError(ex, "Shop profile cache invalidation failed for {ShopId}; Wolverine will retry", message.ShopId);
            throw;
        }
    }
}
