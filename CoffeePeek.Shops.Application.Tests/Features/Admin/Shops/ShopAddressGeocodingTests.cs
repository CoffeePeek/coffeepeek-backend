using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public class ShopAddressGeocodingTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop _shop =
        new(null, "One Mo", null, PriceRange.Moderate, Guid.NewGuid());
    private readonly City _city = new("Минск");
    private Guid _cityId => _city.Id;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Mock<ICoffeeShopRepository> _shops = new();
    private readonly Mock<IQueryCityRepository> _cities = new();
    private readonly Mock<IAddressGeocoder> _geocoder = new(MockBehavior.Strict);
    private readonly Mock<IMessageBus> _bus = new();

    public ShopAddressGeocodingTests()
    {
        _shop.SetLocation(_cityId, "Old address", 53.9m, 27.5m);
        _shop.AssignOwner(_ownerId);
        _shops.Setup(r => r.GetByIdWithCatalogsAsync(_shop.Id, _ct)).ReturnsAsync(_shop);
        _shops.Setup(r => r.GetByIdWithCatalogsForOwnerAsync(_shop.Id, _ownerId, _ct)).ReturnsAsync(_shop);
        _cities.Setup(c => c.GetById(_cityId, _ct)).ReturnsAsync(_city);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedAddress_WithPreviousPin_UsesFreshCoordinatesInResponse(bool owner)
    {
        _geocoder.Setup(g => g.GeocodeAsync("Минск, New address", _ct))
            .ReturnsAsync(new GeocodingResult(53.92m, 27.61m));

        var response = await UpdateAsync(owner, new(_cityId, " New address ", 53.9m, 27.5m));

        Assert.Equal("New address", _shop.Location.Address);
        Assert.Equal(53.92m, _shop.Location.Latitude);
        Assert.Equal(27.61m, _shop.Location.Longitude);
        Assert.Equal(53.92m, response.Location!.Latitude);
        Assert.Equal(27.61m, response.Location.Longitude);
        Assert.Equal(1, response.ProfileRevision);
        _geocoder.Verify(g => g.GeocodeAsync("Минск, New address", _ct), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(e => e.ShopId == _shop.Id)), Times.Once);
    }

    [Fact]
    public async Task ChangedCity_WithSameStreet_GeocodesAgain()
    {
        var otherCity = new City("Брест");
        _cities.Setup(c => c.GetById(otherCity.Id, _ct)).ReturnsAsync(otherCity);
        _geocoder.Setup(g => g.GeocodeAsync("Брест, Old address", _ct))
            .ReturnsAsync(new GeocodingResult(54.2m, 28.3m));

        await UpdateAsync(false, new(otherCity.Id, "Old address", 53.9m, 27.5m));

        Assert.Equal(54.2m, _shop.Location.Latitude);
        _geocoder.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameAddress_AllowsManualPinCorrectionWithoutGeocoding(bool owner)
    {
        await UpdateAsync(owner, new(_cityId, " Old address ", 53.91m, 27.51m));

        Assert.Equal(53.91m, _shop.Location.Latitude);
        Assert.Equal(27.51m, _shop.Location.Longitude);
        _geocoder.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeocodingFailure_DoesNotMutateAnyProfileFields(bool owner, bool unavailable)
    {
        var setup = _geocoder.Setup(g => g.GeocodeAsync("Минск, New address", _ct));
        if (unavailable) setup.ThrowsAsync(new HttpRequestException("Yandex unavailable"));
        else setup.ReturnsAsync((GeocodingResult?)null);

        Func<Task> update = () => UpdateAsync(owner, new(_cityId, "New address", 53.9m, 27.5m));
        if (unavailable) await Assert.ThrowsAsync<ServiceUnavailableException>(update);
        else await Assert.ThrowsAsync<ValidationException>(update);

        Assert.Equal("One Mo", _shop.Name);
        Assert.Null(_shop.Description);
        Assert.Equal("Old address", _shop.Location.Address);
        Assert.Equal(53.9m, _shop.Location.Latitude);
        Assert.Equal(27.5m, _shop.Location.Longitude);
        Assert.Equal(0, _shop.ProfileRevision);
        Assert.Empty(_shop.Schedules);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StaleRevision_IsRejectedBeforeGeocoding()
    {
        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(false,
            new(_cityId, "New address", 53.9m, 27.5m), expectedRevision: 1));

        _geocoder.VerifyNoOtherCalls();
        Assert.Equal("Old address", _shop.Location.Address);
    }

    private async Task<AdminPublishedShopDto> UpdateAsync(bool owner, ShopLocationPatch location, long? expectedRevision = null)
    {
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" });
        var equipment = Mock.Of<IQueryEquipmentRepository>();
        var beans = Mock.Of<IQueryCoffeeBeanRepository>();
        var roasters = Mock.Of<IQueryRoasterRepository>();
        var brew = Mock.Of<IQueryBrewMethodRepository>();
        var schedules = new[] { new CoffeePeek.Contract.Dtos.Schedule.ScheduleDto(DayOfWeek.Monday, true, []) };
        var response = owner
            ? await UpdateOwnerCoffeeShopHandler.Handle(
                new(_shop.Id, _ownerId, "Edited name", "Edited description", null, null, null, null,
                    location, schedules, null) { ExpectedRevision = expectedRevision },
                _shops.Object, _cities.Object, equipment, beans, roasters, brew, _bus.Object, media, _geocoder.Object, _ct)
            : await UpdateAdminCoffeeShopHandler.Handle(
                new(_shop.Id, "Edited name", "Edited description", ContractPriceRange.Expensive, null,
                    location, null, schedules, null) { ExpectedRevision = expectedRevision },
                _shops.Object, _cities.Object, equipment, beans, roasters, brew, _bus.Object, media, _geocoder.Object, _ct);
        Assert.True(response.IsSuccess);
        return response.Data!;
    }
}
