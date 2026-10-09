using System.Text.Json;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Consumers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class CheckInCacheInvalidationRequestedHandlerTests
{
    [Fact]
    public async Task Handle_DeletesBothCaches_AndRepeatDeliveryIsHarmless()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        var message = new CheckInCacheInvalidationRequested(Guid.NewGuid());

        await CheckInCacheInvalidationRequestedHandler.Handle(
            message, cache.Object, NullLogger<CheckInCacheInvalidationRequestedHandler>.Instance,
            TestContext.Current.CancellationToken);
        await CheckInCacheInvalidationRequestedHandler.Handle(
            message, cache.Object, NullLogger<CheckInCacheInvalidationRequestedHandler>.Instance,
            TestContext.Current.CancellationToken);

        cache.Verify(c => c.RemoveAsync(
            It.Is<CacheKey>(k => k.Key == "shop:public:platform-stats"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(c => c.RemoveByPatternAsync("shop:search:*", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_WhenRedisFails_PropagatesForWolverineRetry()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveByPatternAsync("shop:search:*", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var action = () => CheckInCacheInvalidationRequestedHandler.Handle(
            new CheckInCacheInvalidationRequested(Guid.NewGuid()), cache.Object,
            NullLogger<CheckInCacheInvalidationRequestedHandler>.Instance,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_WithOwnerIdentifiers_InvalidatesDetailAndVisitKeys()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await CheckInCacheInvalidationRequestedHandler.Handle(
            new CheckInCacheInvalidationRequested(Guid.NewGuid(), shopId, ownerId), cache.Object,
            NullLogger<CheckInCacheInvalidationRequestedHandler>.Instance,
            TestContext.Current.CancellationToken);

        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(shopId), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Visited(ownerId), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.VisitStatistics(ownerId), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.ListPattern(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OldQueuedMessage_DeserializesWithoutOptionalIdentifiers()
    {
        var id = Guid.NewGuid();
        var message = JsonSerializer.Deserialize<CheckInCacheInvalidationRequested>(
            $$"""{"CheckInId":"{{id}}"}""");

        Assert.NotNull(message);
        Assert.Equal(id, message.CheckInId);
        Assert.Null(message.ShopId);
        Assert.Null(message.UserId);
    }
}
