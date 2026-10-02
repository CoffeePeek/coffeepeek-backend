using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Common.Models;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Domain.Aggregates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationGeocodingRequestedHandlerTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task StaleAddress_SkipsGeocoderAndUpdate()
    {
        var shops = new Mock<IModerationShopRepository>();
        var roasters = new Mock<IModerationRoasterRepository>();
        var geocoder = new Mock<IYandexGeocodingService>();
        var request = new ModerationGeocodingRequested(ModerationGeocodingKind.Shop, Guid.NewGuid(), "Old address");
        shops.Setup(x => x.IsAwaitingGeocodingAsync(request.EntityId, request.SubmittedAddress, _ct))
            .ReturnsAsync(false);

        await ModerationGeocodingRequestedHandler.Handle(request, shops.Object, roasters.Object,
            geocoder.Object, NullLogger<ModerationGeocodingRequested>.Instance, _ct);

        geocoder.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        shops.Verify(x => x.TryApplyGeocodingAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulGeocode_UsesAddressGuardedUpdate()
    {
        var shops = new Mock<IModerationShopRepository>();
        var roasters = new Mock<IModerationRoasterRepository>();
        var geocoder = new Mock<IYandexGeocodingService>();
        var request = new ModerationGeocodingRequested(ModerationGeocodingKind.Roaster, Guid.NewGuid(), "Exact address");
        roasters.Setup(x => x.IsAwaitingGeocodingAsync(request.EntityId, request.SubmittedAddress, _ct))
            .ReturnsAsync(true);
        geocoder.Setup(x => x.GeocodeAsync(request.SubmittedAddress, _ct))
            .ReturnsAsync(new GeocodingResult(53.9m, 27.5m));
        roasters.Setup(x => x.TryApplyGeocodingAsync(request.EntityId, request.SubmittedAddress,
                53.9m, 27.5m, _ct))
            .ReturnsAsync(true);

        await ModerationGeocodingRequestedHandler.Handle(request, shops.Object, roasters.Object,
            geocoder.Object, NullLogger<ModerationGeocodingRequested>.Instance, _ct);

        roasters.Verify(x => x.TryApplyGeocodingAsync(request.EntityId, request.SubmittedAddress,
            53.9m, 27.5m, _ct), Times.Once);
    }

    [Fact]
    public async Task TransientGeocoderFailure_PropagatesForWolverineRetry()
    {
        var shops = new Mock<IModerationShopRepository>();
        var roasters = new Mock<IModerationRoasterRepository>();
        var geocoder = new Mock<IYandexGeocodingService>();
        var request = new ModerationGeocodingRequested(ModerationGeocodingKind.Shop, Guid.NewGuid(), "Address");
        shops.Setup(x => x.IsAwaitingGeocodingAsync(request.EntityId, request.SubmittedAddress, _ct))
            .ReturnsAsync(true);
        geocoder.Setup(x => x.GeocodeAsync(request.SubmittedAddress, _ct))
            .ThrowsAsync(new HttpRequestException("Yandex unavailable"));

        var act = () => ModerationGeocodingRequestedHandler.Handle(request, shops.Object, roasters.Object,
            geocoder.Object, NullLogger<ModerationGeocodingRequested>.Instance, _ct);

        await act.Should().ThrowAsync<HttpRequestException>();
        shops.Verify(x => x.TryApplyGeocodingAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoResult_LeavesAddressForModerator()
    {
        var shops = new Mock<IModerationShopRepository>();
        var roasters = new Mock<IModerationRoasterRepository>();
        var geocoder = new Mock<IYandexGeocodingService>();
        var request = new ModerationGeocodingRequested(ModerationGeocodingKind.Shop, Guid.NewGuid(), "Unmapped address");
        shops.Setup(x => x.IsAwaitingGeocodingAsync(request.EntityId, request.SubmittedAddress, _ct))
            .ReturnsAsync(true);
        geocoder.Setup(x => x.GeocodeAsync(request.SubmittedAddress, _ct))
            .ReturnsAsync((GeocodingResult?)null);

        await ModerationGeocodingRequestedHandler.Handle(request, shops.Object, roasters.Object,
            geocoder.Object, NullLogger<ModerationGeocodingRequested>.Instance, _ct);

        shops.Verify(x => x.TryApplyGeocodingAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
