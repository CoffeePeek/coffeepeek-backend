using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Public;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Public.Stats;

public class GetPublicStatsHandlerTests
{
    [Fact]
    public async Task PublicStatsReadCurrentEligibilityEvenWhenOldCacheContainsPrivateCounts()
    {
        var repository=new Mock<IPublicStatsQueryRepository>();var cache=new Mock<ICacheService>();
        cache.Setup(c=>c.GetAsync<PublicPlatformStatsDto>(It.IsAny<CacheKey>(),It.IsAny<CancellationToken>())).ReturnsAsync(new PublicPlatformStatsDto(12,999,5));
        repository.SetupSequence(r=>r.GetStatsAsync(default)).ReturnsAsync(new PublicPlatformStats(12,2,4)).ReturnsAsync(new PublicPlatformStats(12,1,3));
        var before=await GetPublicStatsHandler.Handle(new GetPublicStatsQuery(),repository.Object,cache.Object,default);
        var after=await GetPublicStatsHandler.Handle(new GetPublicStatsQuery(),repository.Object,cache.Object,default);
        Assert.Equal(2,before.Data.TotalCheckIns);Assert.Equal(1,after.Data.TotalCheckIns);Assert.Equal(3,after.Data.AverageRating);
        cache.Verify(c=>c.GetAsync<PublicPlatformStatsDto>(It.IsAny<CacheKey>(),It.IsAny<CancellationToken>()),Times.Never());
    }
}
