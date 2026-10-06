using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.Collections.Generic;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;

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
    private readonly Mock<ICoffeeCatalogStore> _coffees = new();

    public GetRoasterByIdHandlerTests()
    {
        _coffees.Setup(c => c.RoasterCountsAsync(It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid[] ids, CancellationToken _) =>
                new Dictionary<Guid, (int CoffeeShopsCount, int CoffeeProductsCount)> { [ids[0]] = (0, 3) });
    }

    [Fact]
    public async Task Handle_MissingRoaster_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdWithShopsAndPhotosAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Roaster)null);

        var result = await GetRoasterByIdHandler.Handle(
            new GetRoasterByIdQuery(Guid.NewGuid()), _repo.Object, _media, _coffees.Object, _ct);

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
            new GetRoasterByIdQuery(roaster.Id), _repo.Object, _media, _coffees.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.GetType().GetProperty("CityId")!.GetValue(result.Data).Should().Be(cityId);
        result.Data.Name.Should().Be("Coffee Circus");
        result.Data.About.Should().Be("Small-batch roaster.");
        result.Data.Location!.Address.Should().Be("1 Main St");
        result.Data.Contact!.InstagramLink.Should().Be("https://instagram.com/roaster");
        result.Data.Shops.Should().BeEmpty();
        result.Data.CoffeeShopsCount.Should().Be(0);
        result.Data.CoffeeProductsCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_RoasterWithPhoto_PopulatesVariantUrlsFallingBackToFullUrl()
    {
        var roaster = new Roaster("Photo Roaster");
        roaster.ReplacePhotos([new RoasterPhoto("r.jpg", "image/jpeg", "roasters/r.jpg", 10, Guid.NewGuid(), 0)]);

        _repo.Setup(r => r.GetByIdWithShopsAndPhotosAsync(roaster.Id, _ct)).ReturnsAsync(roaster);

        var result = await GetRoasterByIdHandler.Handle(
            new GetRoasterByIdQuery(roaster.Id), _repo.Object, _media, _coffees.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        var photo = result.Data!.Photos.Should().ContainSingle().Subject;
        photo.FullUrl.Should().Be("https://media.coffeepeek.by/coffeepeek.shops/roasters/r.jpg");
        photo.Urls.Should().NotBeNull();
        photo.Urls.Card.Should().Be(photo.FullUrl);
        photo.Urls.Thumbnail.Should().Be(photo.FullUrl);
        photo.Urls.Detail.Should().Be(photo.FullUrl);
        photo.Urls.Fullscreen.Should().Be(photo.FullUrl);
    }
}
