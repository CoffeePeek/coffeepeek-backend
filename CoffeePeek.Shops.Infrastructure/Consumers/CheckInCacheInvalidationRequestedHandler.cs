using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Observability;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public sealed class CheckInCacheInvalidationRequestedHandler
{
    public static void Configure(HandlerChain chain)
    {
        // Durable scheduled retries cover transient Redis outages without delaying the POST.
        // After exhaustion Wolverine moves the message to its error queue for inspection.
        chain.OnException<RedisException>().ScheduleRetry(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    }

    public static async Task Handle(
        CheckInCacheInvalidationRequested message,
        IStrictCacheInvalidator cache,
        ILogger<CheckInCacheInvalidationRequestedHandler> logger,
        CancellationToken ct)
    {
        using var activity = ShopsTelemetry.ActivitySource.StartActivity("shops.cache.invalidate.check_in");
        activity?.SetTag("check_in.id", message.CheckInId);
        try
        {
            // Both operations are idempotent. A failed Redis command must make Wolverine retry
            // the durable local message instead of acknowledging a stale cache as invalidated.
            await cache.RemoveAsync(CacheKey.Shop.PublicPlatformStats(), ct);
            await cache.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShopsTelemetry.CheckInCacheInvalidationFailed();
            logger.LogError(ex, "Check-in cache invalidation failed for {CheckInId}; Wolverine will retry", message.CheckInId);
            throw;
        }
    }
}
