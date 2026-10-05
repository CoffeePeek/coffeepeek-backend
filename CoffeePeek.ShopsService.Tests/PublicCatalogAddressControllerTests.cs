using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Tests;

public sealed class PublicCatalogAddressControllerTests
{
    private readonly FakeAddresses _addresses = new();
    private readonly FakeZones _zones = new();
    private readonly FakeCities _cities = new();

    [Fact]
    public async Task ZoneBySlug_UnknownAddress_ReturnsNotFound()
    {
        var result = await ZoneController().GetBySlug("nemiga", TestContext.Current.CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
        _addresses.LastKind.Should().Be(PublicAddressKind.CoffeeZone);
    }

    [Theory]
    [InlineData(CoffeeZoneStatus.Draft)]
    [InlineData(CoffeeZoneStatus.Archived)]
    public async Task ZoneBySlug_ZoneNotPublishedOnDataRead_ReturnsNotFound(CoffeeZoneStatus status)
    {
        // The address resolved while the zone was Published, but it was hidden before the data read.
        var zone = Zone(status);
        _zones.Zone = zone;
        _addresses.Resolved = ZoneAddress(zone.Id);

        var result = await ZoneController().GetBySlug("nemiga", TestContext.Current.CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ZoneBySlug_Published_ReturnsMapZoneEnvelope()
    {
        var zone = Zone(CoffeeZoneStatus.Published);
        var address = ZoneAddress(zone.Id);
        _zones.Zone = zone;
        _addresses.Resolved = address;

        var result = await ZoneController().GetBySlug("nemiga", TestContext.Current.CancellationToken);

        var envelope = result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<PublicZoneDto>().Subject;
        envelope.Name.Should().Be(zone.Name);
        envelope.Description.Should().Be(zone.Description);
        envelope.Latitude.Should().Be(zone.CenterLatitude);
        envelope.Longitude.Should().Be(zone.CenterLongitude);
        envelope.RadiusMeters.Should().Be(zone.RadiusMeters);
        envelope.ShopCount.Should().Be(zone.ShopCount);
        envelope.Color.Should().Be(zone.Color);
        envelope.Polygon.Should().HaveCount(zone.Polygon.Length);
        envelope.Polygon[0].Should().Be(new MapCoffeeZonePointDto(zone.Polygon[0].Latitude, zone.Polygon[0].Longitude));
        envelope.Address.Should().Be(PublicAddressDto.From(address));
        envelope.Address.CanonicalPath.Should().Be("/coffee-zones/nemiga");
    }

    [Fact]
    public async Task ZoneBySlug_ZoneMissingOnDataRead_ReturnsNotFound()
    {
        _addresses.Resolved = ZoneAddress(Guid.NewGuid());

        var result = await ZoneController().GetBySlug("nemiga", TestContext.Current.CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CityBySlug_UnknownAddress_ReturnsNotFound()
    {
        var result = await CityController().GetBySlug("minsk", TestContext.Current.CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
        _addresses.LastKind.Should().Be(PublicAddressKind.City);
    }

    [Fact]
    public async Task CityBySlug_CityDeletedBetweenResolveAndRead_ReturnsNotFound()
    {
        _addresses.Resolved = CityAddress(Guid.NewGuid());

        var result = await CityController().GetBySlug("minsk", TestContext.Current.CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CityBySlug_Exists_ReturnsCityEnvelope()
    {
        var city = new City("Минск");
        var address = CityAddress(city.Id);
        _cities.City = city;
        _addresses.Resolved = address;

        var result = await CityController().GetBySlug("minsk", TestContext.Current.CancellationToken);

        var envelope = result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<PublicCityDto>().Subject;
        envelope.Name.Should().Be(city.Name);
        envelope.Address.Should().Be(PublicAddressDto.From(address));
        envelope.Address.CanonicalPath.Should().Be("/cities/minsk");
    }

    private PublicCoffeeZoneAddressesController ZoneController() => new(_addresses, _zones);

    private PublicCityAddressesController CityController() => new(_addresses, _cities);

    private static AdminCoffeeZoneDto Zone(CoffeeZoneStatus status) => new(Guid.NewGuid(), Guid.NewGuid(), "Nemiga",
        "Old town", 53.9m, 27.56m, 400, status, 3, DateTime.UtcNow, null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400), "#A1B2C3");

    private static PublicAddressMetadata ZoneAddress(Guid id) => new(id, "nemiga", "/coffee-zones/nemiga", 1);

    private static PublicAddressMetadata CityAddress(Guid id) => new(id, "minsk", "/cities/minsk", 1);

    private sealed class FakeAddresses : IPublicAddressService
    {
        public PublicAddressMetadata? Resolved { get; set; }
        public PublicAddressKind? LastKind { get; private set; }

        public Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct)
        {
            LastKind = kind;
            return Task.FromResult(Resolved);
        }

        public Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PublicAddressMetadata[]> GetForReadAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid[]> ResolveIdsAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct) => throw new NotSupportedException();

        public Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request,
            Guid actorId, CancellationToken ct) => throw new NotSupportedException();

        public Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun,
            Guid actorId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeZones : ICoffeeZoneQueries
    {
        public AdminCoffeeZoneDto? Zone { get; set; }

        // Only returns the zone for the id the controller resolved, so a wrong id reads as missing.
        public Task<AdminCoffeeZoneDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Zone?.Id == id ? Zone : null);

        public Task<AdminCoffeeZoneDto[]> GetAllAsync(Guid? cityId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CoffeeZoneMembershipPreviewDto?> PreviewMembershipAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CoffeeZoneCandidateDto[]> GenerateCandidatesAsync(Guid cityId, int radiusMeters, int minShops,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCities : IQueryCityRepository
    {
        public City? City { get; set; }

        public Task<City?> GetById(Guid id, CancellationToken ct = default) =>
            Task.FromResult(City?.Id == id ? City : null);

        public Task<City[]> GetAll(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> Exists(Guid cityId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<City?> GetByName(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
