using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationSubmissionGeocodingTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShopSubmission_QueuesExactAddressAfterSavingUnvalidatedRow()
    {
        var id = Guid.NewGuid();
        var repository = new Mock<IModerationShopRepository>();
        var creation = new Mock<IModerationShopCreationService>();
        creation.Setup(x => x.Create(It.IsAny<SendCoffeeShopToModerationCommand>(), _ct)).ReturnsAsync(id);
        var command = new SendCoffeeShopToModerationCommand
        {
            Name = "Cafe", Address = "Exact address", CityId = Guid.NewGuid()
        };

        var (response, parseMenu, geocode) = await SendCoffeeShopToModerationHandler.Handle(
            command, repository.Object, creation.Object,
            NullLogger<SendCoffeeShopToModerationCommand>.Instance, _ct);

        response.Data!.ShopId.Should().Be(id);
        response.Data.IsAddressValidated.Should().BeFalse();
        response.Message.Should().Contain("pending");
        parseMenu.Should().BeNull();
        geocode.Should().Be(new ModerationGeocodingRequested(ModerationGeocodingKind.Shop, id, command.Address));
        creation.Verify(x => x.Create(command, _ct), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task RoasterWithoutAddress_DoesNotQueueGeocoding(string? address)
    {
        var repository = new Mock<IModerationRoasterRepository>();
        var lookup = new Mock<IRoasterExistenceLookup>();
        var uow = new Mock<IUnitOfWork>();
        var command = new SubmitRoasterCommand { Name = "Roaster", Address = address };

        var (response, request) = await SubmitRoasterHandler.Handle(command, repository.Object,
            lookup.Object, uow.Object, NullLogger<SubmitRoasterCommand>.Instance, _ct);

        response.Data!.IsAddressValidated.Should().BeFalse();
        request.Should().BeNull();
    }

    [Fact]
    public async Task RoasterWithPartialAddress_IsRejectedBeforeSaving()
    {
        var repository = new Mock<IModerationRoasterRepository>(MockBehavior.Strict);
        var lookup = new Mock<IRoasterExistenceLookup>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);

        foreach (var command in new[]
                 {
                     new SubmitRoasterCommand { Name = "Roaster", CityId = Guid.NewGuid() },
                     new SubmitRoasterCommand { Name = "Roaster", Address = "Street" }
                 })
        {
            var (response, request) = await SubmitRoasterHandler.Handle(command, repository.Object,
                lookup.Object, uow.Object, NullLogger<SubmitRoasterCommand>.Instance, _ct);
            response.StatusCode.Should().Be(400);
            request.Should().BeNull();
        }
    }

    [Fact]
    public async Task RoasterWithAddress_SavesUnvalidatedAndQueuesExactAddress()
    {
        ModerationRoaster? saved = null;
        var repository = new Mock<IModerationRoasterRepository>();
        repository.Setup(x => x.AddAsync(It.IsAny<ModerationRoaster>()))
            .Callback<ModerationRoaster>(x => saved = x)
            .Returns(Task.CompletedTask);
        var lookup = new Mock<IRoasterExistenceLookup>();
        var uow = new Mock<IUnitOfWork>();
        var command = new SubmitRoasterCommand { Name = "Roaster", Address = "Exact address", CityId = Guid.NewGuid() };

        var (response, request) = await SubmitRoasterHandler.Handle(command, repository.Object,
            lookup.Object, uow.Object, NullLogger<SubmitRoasterCommand>.Instance, _ct);

        saved!.Location!.IsAddressValidated.Should().BeFalse();
        response.Data!.RoasterId.Should().Be(saved.Id);
        response.Message.Should().Contain("pending");
        request.Should().Be(new ModerationGeocodingRequested(
            ModerationGeocodingKind.Roaster, saved.Id, command.Address!));
    }
}
