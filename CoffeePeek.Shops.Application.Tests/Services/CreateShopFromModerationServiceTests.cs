using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class CreateShopFromModerationServiceTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepoMock = new();
    private readonly Mock<IQueryCoffeeBeanRepository> _coffeeBeanRepoMock = new();
    private readonly Mock<IQueryEquipmentRepository> _equipmentRepoMock = new();
    private readonly Mock<IQueryRoasterRepository> _roasterRepoMock = new();
    private readonly Mock<IQueryBrewMethodRepository> _brewMethodRepoMock = new();
    private readonly Mock<IApplyShopMenuService> _applyMenuMock = new();
    private readonly Mock<Wolverine.IMessageBus> _bus = new();
    private readonly Mock<ILogger<CreateShopFromModerationService>> _loggerMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private CreateShopFromModerationService CreateSut() =>
        new CreateShopFromModerationService(
            _shopRepoMock.Object,
            _coffeeBeanRepoMock.Object,
            _equipmentRepoMock.Object,
            _roasterRepoMock.Object,
            _brewMethodRepoMock.Object,
            _applyMenuMock.Object,
            _bus.Object,
            _loggerMock.Object);

    private static ShopDto CreateMinimalShopDto() =>
        new ShopDto
        {
            Name = "Test Shop",
            Description = null,
            PriceRange = CoffeePeek.Contract.Enums.PriceRange.Moderate,
            CityId = Guid.NewGuid(),
            Location = null,
            ShopContact = null,
            Equipments = null,
            BrewMethods = null,
            Roasters = null,
            CoffeeBeans = null,
            Schedules = null,
            Photos = [],
            CheckIns = []
        };

    [Fact]
    public async Task CreateShopFromApprovedEventAsync_WhenModerationIdIsNew_AddsShopAndQueuesInvalidation()
    {
        // Arrange
        _shopRepoMock
            .Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct))
            .ReturnsAsync((Guid?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.CreateShopFromApprovedEventAsync(
            CreateMinimalShopDto(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            _ct);

        // Wolverine's EF middleware saves the shop and outbox together after the handler returns.
        result.Should().NotBe(Guid.Empty);
        _shopRepoMock.Verify(r => r.Add(It.IsAny<CoffeeShop>()), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.ShopId == result)), Times.Once);
    }

    [Fact]
    public async Task CreateShopFromApprovedEventAsync_WhenModerationIdAlreadyExists_ReturnsExistingIdAndDoesNotAddOrSave()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        _shopRepoMock
            .Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct))
            .ReturnsAsync(existingId);

        var sut = CreateSut();

        // Act
        var result = await sut.CreateShopFromApprovedEventAsync(
            CreateMinimalShopDto(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            _ct);

        // Assert — duplicate guard returns the existing shop id without Add or SaveChangesAsync
        result.Should().Be(existingId);
        _shopRepoMock.Verify(r => r.Add(It.IsAny<CoffeeShop>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task CreateShopFromApprovedEventAsync_CopiesCoffeeFocusWithoutAssigningTags()
    {
        CoffeeShop? added = null;
        _shopRepoMock
            .Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct))
            .ReturnsAsync((Guid?)null);
        _shopRepoMock
            .Setup(r => r.Add(It.IsAny<CoffeeShop>()))
            .Callback<CoffeeShop>(shop => added = shop);

        var dto = CreateMinimalShopDto();
        dto.Type = CoffeePeek.Contract.Enums.CoffeeShopType.Specialty;

        var result = await CreateSut().CreateShopFromApprovedEventAsync(
            dto,
            Guid.NewGuid(),
            Guid.NewGuid(),
            _ct);

        result.Should().NotBe(Guid.Empty);
        added.Should().NotBeNull();
        added!.Type.Should().Be(CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeFocusType.Specialty);
        added.ShopTags.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateShopFromApprovedEventAsync_LocationWithoutCoordinates_CreatesDraftLocation()
    {
        CoffeeShop? added = null;
        _shopRepoMock.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _shopRepoMock.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(shop => added = shop);
        var dto = CreateMinimalShopDto();
        dto.Location = new CoffeePeek.Contract.Dtos.Shop.LocationDto { Address = "Nemiga 5" };

        await CreateSut().CreateShopFromApprovedEventAsync(dto, Guid.NewGuid(), Guid.NewGuid(), _ct);

        added!.Location!.Latitude.Should().BeNull();
        added.Location.Address.Should().Be("Nemiga 5");
    }
}
