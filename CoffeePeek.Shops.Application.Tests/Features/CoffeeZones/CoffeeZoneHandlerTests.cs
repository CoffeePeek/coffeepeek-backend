using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeZones;

public sealed class CoffeeZoneHandlerTests
{
    [Fact]
    public async Task Update_StoresRequestedColor()
    {
        var cityId = Guid.NewGuid();
        var zone = new CoffeeZone(cityId, "Center", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
        var cities = new Mock<IQueryCityRepository>();
        var repository = new Mock<ICoffeeZoneRepository>();
        var queries = new Mock<ICoffeeZoneQueries>();
        var unitOfWork = new Mock<IUnitOfWork>();
        cities.Setup(r => r.Exists(cityId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.Setup(r => r.GetByIdAsync(zone.Id, It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        queries.Setup(q => q.GetByIdAsync(zone.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AdminCoffeeZoneDto(zone.Id, cityId, zone.Name, null,
                zone.CenterLatitude, zone.CenterLongitude, zone.RadiusMeters, zone.Status, 0,
                default, null, zone.Polygon.ToArray(), zone.Color));

        var result = await UpdateCoffeeZoneHandler.Handle(
            new UpdateCoffeeZoneCommand(zone.Id, cityId, "Center", null, zone.Polygon.ToArray(), "#a1b2c3"),
            cities.Object, repository.Object, queries.Object, unitOfWork.Object, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("#A1B2C3", zone.Color);
        Assert.Equal(zone.Color, result.Data!.Color);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPrimaryOverride_ReplacesPreviousPrimary()
    {
        var cityId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var zone = new CoffeeZone(cityId, "Center", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
        var previous = new CoffeeZoneMembershipOverride(Guid.NewGuid(), shopId, CoffeeZoneMembershipOverrideKind.Primary);
        var repository = new Mock<ICoffeeZoneRepository>();
        var shops = new Mock<IQueryCoffeeShopRepository>();
        var queries = new Mock<ICoffeeZoneQueries>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var preview = new CoffeeZoneMembershipPreviewDto(
            new AdminCoffeeZoneDto(zone.Id, cityId, zone.Name, null, 53.9m, 27.56m, 400,
                CoffeeZoneStatus.Draft, 1, default, null, [], zone.Color),
            []);

        repository.Setup(r => r.GetByIdAsync(zone.Id, It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        repository.Setup(r => r.GetPrimaryOverrideAsync(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        repository.Setup(r => r.GetOverrideAsync(zone.Id, shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoffeeZoneMembershipOverride?)null);
        shops.Setup(r => r.GetCityIdAsync(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(cityId);
        queries.Setup(q => q.PreviewMembershipAsync(zone.Id, It.IsAny<CancellationToken>())).ReturnsAsync(preview);

        var result = await SetCoffeeZoneMembershipOverrideHandler.Handle(
            new SetCoffeeZoneMembershipOverrideCommand(zone.Id, shopId, CoffeeZoneMembershipOverrideKind.Primary),
            shops.Object, repository.Object, queries.Object, unitOfWork.Object, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(r => r.RemoveOverride(previous), Times.Once);
        repository.Verify(r => r.AddOverride(It.Is<CoffeeZoneMembershipOverride>(x =>
            x.ZoneId == zone.Id && x.ShopId == shopId && x.Kind == CoffeeZoneMembershipOverrideKind.Primary)), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetOverride_ForShopInAnotherCity_ReturnsBadRequest()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
        var repository = new Mock<ICoffeeZoneRepository>();
        var shops = new Mock<IQueryCoffeeShopRepository>();
        var queries = new Mock<ICoffeeZoneQueries>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var shopId = Guid.NewGuid();

        repository.Setup(r => r.GetByIdAsync(zone.Id, It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        shops.Setup(r => r.GetCityIdAsync(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        var result = await SetCoffeeZoneMembershipOverrideHandler.Handle(
            new SetCoffeeZoneMembershipOverrideCommand(zone.Id, shopId, CoffeeZoneMembershipOverrideKind.Include),
            shops.Object, repository.Object, queries.Object, unitOfWork.Object, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
