using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public sealed class DrinkCatalogCacheInvalidationRequestedHandlerTests
{
    [Fact]
    public async Task Redelivery_RemovesCatalogAndDetailCachesAgain()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        for (var attempt = 0; attempt < 2; attempt++)
            await DrinkCatalogCacheInvalidationRequestedHandler.Handle(
                new DrinkCatalogCacheInvalidationRequested(), cache.Object,
                NullLogger<DrinkCatalogCacheInvalidationRequestedHandler>.Instance,
                TestContext.Current.CancellationToken);

        cache.Verify(c => c.RemoveAsync(
            It.Is<CacheKey>(key => key.Key == CacheKey.Shop.MenuDrinks().Key),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(c => c.RemoveByPatternAsync(CacheKey.Shop.DetailPattern(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CacheFailure_PropagatesForDurableRetry()
    {
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveByPatternAsync(CacheKey.Shop.DetailPattern(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DrinkCatalogCacheInvalidationRequestedHandler.Handle(
                new DrinkCatalogCacheInvalidationRequested(), cache.Object,
                NullLogger<DrinkCatalogCacheInvalidationRequestedHandler>.Instance,
                TestContext.Current.CancellationToken));
    }
}
