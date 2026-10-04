using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.GetCoffeeShop;

public class GetCoffeeShopHandlerTests
{
    private readonly Mock<ICoffeeShopQueries> _shopQueriesMock = new();
    private readonly Mock<IQueryCheckInRepository> _checkInRepoMock = new();
    private readonly Mock<IQueryReviewRepository> _reviewRepoMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static CoffeeShopDetailsDto BuildShopDto(Guid shopId)
        => new CoffeeShopDetailsDto
        {
            Id = shopId,
            Name = "Test Coffee Shop",
            Reviews = Array.Empty<ReviewDto>()
        };

    [Fact]
    public async Task Handle_AnonymousRequest_ReturnsShopWithoutPersonalization()
    {
        var shopId = Guid.NewGuid();
        var shopDto = BuildShopDto(shopId);

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<CoffeeShopDetailsDto>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(shopDto);

        var query = new GetCoffeeShopQuery(shopId, UserId: null);
        var result = await GetCoffeeShopHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _checkInRepoMock.Object,
            _reviewRepoMock.Object,
            _cacheMock.Object,
            _mapperMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.ShopDto.Id.Should().Be(shopId);
        result.Data.ShopDto.CanCreateReview.Should().BeNull();
        result.Data.ShopDto.ExistingReviewId.Should().BeNull();
        _checkInRepoMock.Verify(
            r => r.Exists(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _reviewRepoMock.Verify(r => r.GetExistingReviewIdByUserAndShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(c => c.GetAsync(
            It.IsAny<CacheKey>(),
            It.IsAny<Func<CancellationToken, Task<CoffeeShopDetailsDto>>>(),
            It.Is<TimeSpan?>(ttl => ttl > TimeSpan.Zero && ttl <= TimeSpan.FromMinutes(1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AuthenticatedRequest_EnrichesWithVisited()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var shopDto = BuildShopDto(shopId);

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<CoffeeShopDetailsDto>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(shopDto);

        _checkInRepoMock
            .Setup(r => r.Exists(userId, shopId, _ct))
            .ReturnsAsync(false);
        _checkInRepoMock
            .Setup(r => r.GetByUserAndShopAsync(userId, shopId, 1, 10, _ct))
            .ReturnsAsync([]);
        _mapperMock
            .Setup(m => m.Map<CheckInDto[]>(It.IsAny<object>()))
            .Returns([]);

        var query = new GetCoffeeShopQuery(shopId, UserId: userId);
        var result = await GetCoffeeShopHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _checkInRepoMock.Object,
            _reviewRepoMock.Object,
            _cacheMock.Object,
            _mapperMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.ShopDto.CanCreateReview.Should().BeTrue();
        result.Data.ShopDto.ExistingReviewId.Should().BeNull();
        result.Data.ShopDto.IsVisited.Should().BeFalse();
        result.Data.ShopDto.UserCheckIns.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CacheSharedAcrossCallers_ReturnsIndependentPublishedReviewAvailability()
    {
        var shopId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        // The user's review is outside the ten cached reviews. Availability must query independently.
        var cached = BuildShopDto(shopId) with
        {
            Reviews = System.Linq.Enumerable.Range(0, 10).Select(_ => new ReviewDto { Id = Guid.NewGuid() }).ToArray()
        };
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<CacheKey>(), It.IsAny<Func<CancellationToken, Task<CoffeeShopDetailsDto>>>(),
            It.IsAny<TimeSpan?>(), _ct)).ReturnsAsync(cached);
        _reviewRepoMock.Setup(r => r.GetExistingReviewIdByUserAndShopAsync(owner, shopId, _ct)).ReturnsAsync(publishedId);
        _reviewRepoMock.Setup(r => r.GetExistingReviewIdByUserAndShopAsync(other, shopId, _ct)).ReturnsAsync((Guid?)null);
        _reviewRepoMock.Setup(r => r.GetHelpfulReviewIds(It.IsAny<Guid>(), It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>(), _ct))
            .ReturnsAsync(new System.Collections.Generic.HashSet<Guid>());
        _checkInRepoMock.Setup(r => r.GetByUserAndShopAsync(It.IsAny<Guid>(), shopId, 1, 10, _ct)).ReturnsAsync([]);
        _mapperMock.Setup(m => m.Map<CheckInDto[]>(It.IsAny<object>())).Returns([]);

        async Task<CoffeeShopDetailsDto> Read(Guid? userId) => (await GetCoffeeShopHandler.Handle(
            new GetCoffeeShopQuery(shopId, userId), _shopQueriesMock.Object, _checkInRepoMock.Object, _reviewRepoMock.Object,
            _cacheMock.Object, _mapperMock.Object, _ct)).Data.ShopDto;

        var first = await Read(owner);
        var second = await Read(other);
        var anonymous = await Read(null);
        first.CanCreateReview.Should().BeFalse();
        first.ExistingReviewId.Should().Be(publishedId);
        first.ExistingReviewId.Should().NotBe(sourceId);
        first.Reviews.Should().NotContain(r => r.Id == publishedId);
        second.CanCreateReview.Should().BeTrue();
        second.ExistingReviewId.Should().BeNull();
        anonymous.Should().BeSameAs(cached);
        anonymous.CanCreateReview.Should().BeNull();
        anonymous.ExistingReviewId.Should().BeNull();
        cached.CanCreateReview.Should().BeNull();
        cached.ExistingReviewId.Should().BeNull();
        cached.Reviews.Should().OnlyContain(r => !r.IsHelpfulByCurrentUser);
        _reviewRepoMock.Verify(r => r.GetExistingReviewIdByUserAndShopAsync(owner, shopId, _ct), Times.Once);
        _reviewRepoMock.Verify(r => r.GetExistingReviewIdByUserAndShopAsync(other, shopId, _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsError()
    {
        var shopId = Guid.NewGuid();

        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<CoffeeShopDetailsDto>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoffeeShopDetailsDto?)null);

        var query = new GetCoffeeShopQuery(shopId, UserId: null);
        var result = await GetCoffeeShopHandler.Handle(
            query,
            _shopQueriesMock.Object,
            _checkInRepoMock.Object,
            _reviewRepoMock.Object,
            _cacheMock.Object,
            _mapperMock.Object,
            _ct);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Shop not found");
    }
}
