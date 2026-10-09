using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Application.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public sealed class ShopProfileCacheInvalidationRequestedHandlerTests
{
    [Fact]
    public void OlderQueuedMessageWithoutBatchField_RemainsReadable()
    {
        var shopId = Guid.NewGuid();
        var message = JsonSerializer.Deserialize<ShopProfileCacheInvalidationRequested>(
            JsonSerializer.Serialize(new { ShopId = shopId }), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(message);
        Assert.Equal(shopId, message.ShopId);
        Assert.Empty(message.AdditionalShopIds);
    }

    [Fact]
    public async Task RepeatDelivery_InvalidatesEveryAffectedCache()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        var message = new ShopProfileCacheInvalidationRequested(Guid.NewGuid());

        for (var attempt = 0; attempt < 2; attempt++)
            await ShopProfileCacheInvalidationRequestedHandler.Handle(
                message, cache.Object, NullLogger<ShopProfileCacheInvalidationRequestedHandler>.Instance,
                TestContext.Current.CancellationToken);

        cache.Verify(c => c.RemoveAsync(
            It.Is<CacheKey>(k => k.Key == CacheKey.Shop.Detail(message.ShopId).Key),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(c => c.RemoveAsync(
            It.Is<CacheKey>(k => k.Key == CacheKey.Shop.PublicPlatformStats().Key),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.ListPattern(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Batch_InvalidatesEachDetailOnceAndGlobalKeysOnce()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var message = new ShopProfileCacheInvalidationRequested(first)
        {
            AdditionalShopIds = [second, first]
        };
        var cache = new Mock<IStrictCacheInvalidator>();

        await ShopProfileCacheInvalidationRequestedHandler.Handle(
            message, cache.Object, NullLogger<ShopProfileCacheInvalidationRequestedHandler>.Instance,
            TestContext.Current.CancellationToken);

        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(first),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(second),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.ListPattern(),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.PublicPlatformStats(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CacheFailure_PropagatesForDurableRetry()
    {
        var observed = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (instrument.Meter.Name == ShopsTelemetry.InstrumentationName)
                subscriber.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "shops.cache_invalidation.failures")
                observed.Enqueue(tags.ToArray());
        });
        listener.Start();

        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ShopProfileCacheInvalidationRequestedHandler.Handle(
                new ShopProfileCacheInvalidationRequested(Guid.NewGuid()), cache.Object,
                NullLogger<ShopProfileCacheInvalidationRequestedHandler>.Instance,
                TestContext.Current.CancellationToken));

        Assert.Contains(observed, tags => tags.Length == 1 && tags[0].Key == "source" &&
            Equals(tags[0].Value, "profile"));
    }
}
