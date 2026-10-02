using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Public;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Public.Stats;

public class GetPublicStatsHandlerTests
{
    [Fact]
    public async Task Handle_WhenCached_ReturnsCachedStatsWithoutQueryingRepository()
    {
        var repositoryMock = new Mock<IPublicStatsQueryRepository>();
        var cacheMock = new Mock<ICacheService>();
        var cached = new PublicPlatformStatsDto(12, 34, 56, 4.2m);

        cacheMock
            .Setup(c => c.GetAsync<PublicPlatformStatsDto>(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var response = await GetPublicStatsHandler.Handle(
            new GetPublicStatsQuery(),
            repositoryMock.Object,
            cacheMock.Object,
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(cached);
        repositoryMock.Verify(r => r.GetStatsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_LoadsFromRepositoryAndStoresResult()
    {
        var repositoryMock = new Mock<IPublicStatsQueryRepository>();
        var cacheMock = new Mock<ICacheService>();

        cacheMock
            .Setup(c => c.GetAsync<PublicPlatformStatsDto>(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicPlatformStatsDto?)null);

        repositoryMock
            .Setup(r => r.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicPlatformStats(12, 34, 56, 4.2m));

        var response = await GetPublicStatsHandler.Handle(
            new GetPublicStatsQuery(),
            repositoryMock.Object,
            cacheMock.Object,
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.TotalCoffeeShops.Should().Be(12);
        response.Data.TotalReviews.Should().Be(34);
        response.Data.TotalCheckIns.Should().Be(56);
        response.Data.AverageRating.Should().Be(4.2m);

        cacheMock.Verify(
            c => c.SetAsync(
                It.Is<CacheKey>(k => k.Key == CacheKey.Shop.PublicPlatformStats().Key),
                It.IsAny<PublicPlatformStatsDto>(),
                null),
            Times.Once);

        CacheKey.Shop.PublicPlatformStats().DefaultTtl.Should().Be(TimeSpan.FromMinutes(5));
    }
}
