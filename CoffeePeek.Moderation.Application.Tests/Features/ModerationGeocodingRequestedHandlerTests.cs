using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Domain.Aggregates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationGeocodingRequestedHandlerTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _entityId = Guid.NewGuid();
    private readonly Guid _cityId = Guid.NewGuid();
    private readonly Mock<IModerationShopRepository> _shops = new();
    private readonly Mock<IModerationRoasterRepository> _roasters = new();
    private readonly Mock<IAddressGeocoder> _geocoder = new(MockBehavior.Strict);
    private readonly Mock<ICityNameLookup> _cities = new(MockBehavior.Strict);

    public ModerationGeocodingRequestedHandlerTests()
    {
        _shops.Setup(r => r.GetAwaitingGeocodingCityIdAsync(_entityId, "Советская, 10", _ct)).ReturnsAsync(_cityId);
        _roasters.Setup(r => r.GetAwaitingGeocodingCityIdAsync(_entityId, "Советская, 10", _ct)).ReturnsAsync(_cityId);
        _cities.Setup(c => c.GetNameAsync(_cityId, _ct)).ReturnsAsync("Брест");
    }

    [Fact]
    public async Task StaleAddress_SkipsCityLookupAndGeocoder()
    {
        await HandleAsync(ModerationGeocodingKind.Shop, "Old address");
        _cities.VerifyNoOtherCalls();
        _geocoder.VerifyNoOtherCalls();
        VerifyNoApply();
    }

    [Theory]
    [InlineData(ModerationGeocodingKind.Shop)]
    [InlineData(ModerationGeocodingKind.Roaster)]
    public async Task SuccessfulGeocode_UsesCityNameAndGuardsCityAndStreet(ModerationGeocodingKind kind)
    {
        _geocoder.Setup(g => g.GeocodeAsync("Брест, Советская, 10", _ct)).ReturnsAsync(new GeocodingResult(52.1m, 23.7m));
        await HandleAsync(kind);
        if (kind == ModerationGeocodingKind.Shop)
            _shops.Verify(r => r.TryApplyGeocodingAsync(_entityId, "Советская, 10", _cityId, 52.1m, 23.7m, _ct), Times.Once);
        else
            _roasters.Verify(r => r.TryApplyGeocodingAsync(_entityId, "Советская, 10", _cityId, 52.1m, 23.7m, _ct), Times.Once);
        _geocoder.VerifyAll();
    }

    [Theory]
    [InlineData("geocoder")]
    [InlineData("city")]
    [InlineData("missing_city")]
    [InlineData("no_result")]
    public async Task FailedLookup_DoesNotApplyCoordinatesOrUseStreetOnlyFallback(string failure)
    {
        if (failure == "city")
            _cities.Setup(c => c.GetNameAsync(_cityId, _ct)).ThrowsAsync(new HttpRequestException("Shops unavailable"));
        else if (failure == "missing_city")
            _cities.Setup(c => c.GetNameAsync(_cityId, _ct)).ReturnsAsync((string?)null);
        else if (failure == "geocoder")
            _geocoder.Setup(g => g.GeocodeAsync("Брест, Советская, 10", _ct)).ThrowsAsync(new HttpRequestException("Yandex unavailable"));
        else
            _geocoder.Setup(g => g.GeocodeAsync("Брест, Советская, 10", _ct)).ReturnsAsync((GeocodingResult?)null);

        if (failure is "city" or "geocoder")
            await Assert.ThrowsAsync<HttpRequestException>(() => HandleAsync(ModerationGeocodingKind.Shop));
        else
            await HandleAsync(ModerationGeocodingKind.Shop);

        if (failure is "city" or "missing_city") _geocoder.VerifyNoOtherCalls();
        VerifyNoApply();
    }

    private Task HandleAsync(ModerationGeocodingKind kind, string address = "Советская, 10") =>
        ModerationGeocodingRequestedHandler.Handle(new(kind, _entityId, address), _shops.Object, _roasters.Object,
            _geocoder.Object, _cities.Object, NullLogger<ModerationGeocodingRequested>.Instance, _ct);

    private void VerifyNoApply() => _shops.Verify(r => r.TryApplyGeocodingAsync(It.IsAny<Guid>(), It.IsAny<string>(),
        It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
}
