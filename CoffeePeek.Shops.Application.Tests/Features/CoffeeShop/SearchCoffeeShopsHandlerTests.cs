using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.SearchCoffeeShops;

public class SearchCoffeeShopsHandlerTests
{
    private readonly Mock<ICoffeeShopQueries> _shopQueriesMock = new();
    private readonly Mock<IQueryCheckInRepository> _visitRepoMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static SearchCoffeeShopsQuery BuildQuery(Guid? userId = null)
        => new SearchCoffeeShopsQuery(UserId: userId);

    [Fact]
    public async Task Handle_AnonymousRequest_ReturnsCachedResponse()
    {
        var shopId = Guid.NewGuid();
        var shop = new ShortShopDto { Id = shopId, Name = "Test Shop" };
        var expectedResponse = new GetCoffeeShopsResponse
        {
            CoffeeShops = [shop],
            TotalItems = 1,
            CurrentPage = 1,
            PageSize = 10,
            TotalPages = 1
        };

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var query = BuildQuery(userId: null);
        var result = await SearchCoffeeShopsHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _visitRepoMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.CoffeeShops.Should().HaveCount(1);
        _visitRepoMock.Verify(
            r => r.GetVisitedShopIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cacheMock.Verify(c => c.GetAsync(
            It.IsAny<CacheKey>(),
            It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
            It.Is<TimeSpan?>(ttl => ttl > TimeSpan.Zero && ttl <= TimeSpan.FromMinutes(1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AuthenticatedRequest_EnrichesWithVisitedFlags()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var shop = new ShortShopDto { Id = shopId, Name = "Test Shop" };
        var response = new GetCoffeeShopsResponse
        {
            CoffeeShops = [shop],
            TotalItems = 1,
            CurrentPage = 1,
            TotalPages = 1
        };

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _visitRepoMock
            .Setup(r => r.GetVisitedShopIdsAsync(userId, _ct))
            .ReturnsAsync(new List<Guid>());

        var query = BuildQuery(userId: userId);
        var result = await SearchCoffeeShopsHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _visitRepoMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.CoffeeShops[0].IsVisited.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenCacheReturnsNull_ReturnsError()
    {
        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetCoffeeShopsResponse?)null);

        var query = BuildQuery(userId: null);
        var result = await SearchCoffeeShopsHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _visitRepoMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Failed to retrieve");
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_SetsPageSizeOnResponse()
    {
        var shop = new ShortShopDto { Id = Guid.NewGuid(), Name = "Test Shop" };
        _shopQueriesMock
            .Setup(q => q.Search(It.IsAny<SearchCoffeeShopsQuery>(), _ct))
            .ReturnsAsync(([shop], 1));

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns((CacheKey _, Func<CancellationToken, Task<GetCoffeeShopsResponse>> factory, TimeSpan? _, CancellationToken ct) => factory(ct));

        var query = new SearchCoffeeShopsQuery(PageNumber: 1, PageSize: 10);
        var result = await SearchCoffeeShopsHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _visitRepoMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.PageSize.Should().Be(10);
        result.Data.CurrentPage.Should().Be(1);
        result.Data.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenCachedResponseOmitsPageSize_CopiesItFromQuery()
    {
        var shop = new ShortShopDto { Id = Guid.NewGuid(), Name = "Test Shop" };
        var staleCache = new GetCoffeeShopsResponse
        {
            CoffeeShops = [shop],
            TotalItems = 1,
            CurrentPage = 1,
            PageSize = 0,
            TotalPages = 1
        };

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<GetCoffeeShopsResponse>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleCache);

        var query = new SearchCoffeeShopsQuery(PageNumber: 1, PageSize: 10);
        var result = await SearchCoffeeShopsHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _visitRepoMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.PageSize.Should().Be(10);
    }

    [Fact]
    public void CreateSearchHash_IncludesTagsAndComputedFilters()
    {
        var tagA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tagB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            UserId: userId,
            Tags: [tagB, tagA],
            IsOpen: true,
            IsNew: false,
            IsVisited: true));

        hash.Should().Contain("tags:");
        hash.Should().Contain("open:True");
        hash.Should().Contain("new:False");
        hash.Should().Contain("visited:True");
        hash.Should().Contain($"uid:{userId}");
    }

    [Fact]
    public void CreateSearchHash_IncludesShopType()
    {
        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            Type: CoffeePeek.Contract.Enums.CoffeeShopType.Specialty));

        hash.Should().Contain("type:1");
    }

    [Fact]
    public void CreateSearchHash_WithoutVisited_OmitsUserId()
    {
        var userId = Guid.NewGuid();
        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            UserId: userId,
            IsOpen: true));

        hash.Should().Contain("open:True");
        hash.Should().NotContain("uid:");
        hash.Should().NotContain("visited:");
    }

    [Fact]
    public void ScheduleCacheTtl_ExpiresAtNextUtcMinute()
    {
        var utcNow = new DateTime(2026, 9, 7, 5, 42, 15, 250, DateTimeKind.Utc);

        var ttl = ShopScheduleCachePolicy.UntilNextUtcMinute(utcNow);

        ttl.Should().Be(TimeSpan.FromSeconds(44.75));
    }
}
