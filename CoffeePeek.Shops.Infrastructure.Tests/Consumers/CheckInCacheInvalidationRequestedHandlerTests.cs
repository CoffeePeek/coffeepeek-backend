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
}
