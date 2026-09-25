using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.CheckIn.GetUserCheckIns;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn.GetUserCheckIns;

public class GetUserCheckInsHandlerTests
{
    private readonly Mock<ICheckInQueries> _checkInQueriesMock = new();
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private GetUserCheckInsHandler CreateSut() =>
        new GetUserCheckInsHandler(_checkInQueriesMock.Object, _shopRepoMock.Object, _mapperMock.Object);

    [Fact]
    public async Task Handle_UsesTotalCountFromQuery_NotPageSliceLength()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var pageItems = new[]
        {
            new CheckInDto { Id = Guid.NewGuid(), UserId = userId, ShopId = shopId },
            new CheckInDto { Id = Guid.NewGuid(), UserId = userId, ShopId = shopId }
        };

        _checkInQueriesMock
            .Setup(q => q.GetByUserId(userId, 1, 2, null, null, _ct))
            .ReturnsAsync((pageItems, 5));

        _shopRepoMock
            .Setup(r => r.GetShopNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>(), _ct))
            .ReturnsAsync(new Dictionary<Guid, string> { [shopId] = "Cafe" });

        var response = await CreateSut().Handle(
            new GetUserCheckInsCommand(userId, PageNumber: 1, PageSize: 2),
            _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalItems.Should().Be(5);
        response.Data.TotalPages.Should().Be(3);
        response.Data.CheckIns.Should().HaveCount(2);
        response.Data.CheckIns.Should().OnlyContain(c => c.ShopName == "Cafe");
    }

    [Fact]
    public async Task Handle_WhenEmpty_ReturnsZeroTotals()
    {
        var userId = Guid.NewGuid();

        _checkInQueriesMock
            .Setup(q => q.GetByUserId(userId, 1, 10, null, null, _ct))
            .ReturnsAsync((Array.Empty<CheckInDto>(), 0));

        _shopRepoMock
            .Setup(r => r.GetShopNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>(), _ct))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var response = await CreateSut().Handle(
            new GetUserCheckInsCommand(userId, PageNumber: 1, PageSize: 10),
            _ct);

        response.Data!.TotalItems.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
        response.Data.CheckIns.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForwardsDateRangeToQuery()
    {
        var userId = Guid.NewGuid();
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        _checkInQueriesMock
            .Setup(q => q.GetByUserId(userId, 1, 100, from, to, _ct))
            .ReturnsAsync((Array.Empty<CheckInDto>(), 0));

        _shopRepoMock
            .Setup(r => r.GetShopNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>(), _ct))
            .ReturnsAsync(new Dictionary<Guid, string>());

        await CreateSut().Handle(new GetUserCheckInsCommand(userId, 1, 100, from, to), _ct);

        _checkInQueriesMock.Verify(q => q.GetByUserId(userId, 1, 100, from, to, _ct), Times.Once);
    }
}
