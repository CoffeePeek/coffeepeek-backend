using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Catalogs.GetRoasterById;

public class GetRoasterByIdHandlerTests
{
    private readonly Mock<IQueryRoasterRepository> _repo = new();
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions
    {
        PublicEndpoint = "https://media.coffeepeek.by",
        ShopBucketName = "coffeepeek.shops"
    });
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_MissingRoaster_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdWithShopsAndPhotosAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Roaster)null);

        var result = await GetRoasterByIdHandler.Handle(
            new GetRoasterByIdQuery(Guid.NewGuid()), _repo.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Handle_ExistingRoaster_ReturnsFullProfile()
    {
        var cityId = Guid.NewGuid();
        var roaster = new Roaster("Coffee Circus");
        roaster.SetAbout("Small-batch roaster.");
        roaster.SetLocation(Location.CreateValidated(cityId, "1 Main St", 53.9m, 27.5m));
        roaster.SetContact(RoasterContact.Create("https://instagram.com/roaster", "https://roaster.com"));

        _repo.Setup(r => r.GetByIdWithShopsAndPhotosAsync(roaster.Id, _ct)).ReturnsAsync(roaster);

        var result = await GetRoasterByIdHandler.Handle(
            new GetRoasterByIdQuery(roaster.Id), _repo.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("Coffee Circus");
        result.Data.About.Should().Be("Small-batch roaster.");
        result.Data.Location!.Address.Should().Be("1 Main St");
        result.Data.Contact!.InstagramLink.Should().Be("https://instagram.com/roaster");
        result.Data.Shops.Should().BeEmpty();
    }
}
