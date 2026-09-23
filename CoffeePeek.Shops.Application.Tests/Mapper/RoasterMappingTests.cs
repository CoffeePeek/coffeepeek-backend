using System;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Shops.Application.Tests.Mapper;

public class RoasterMappingTests
{
    private static readonly MediaPublicUrlOptions Media = new()
    {
        PublicEndpoint = "https://media.coffeepeek.by",
        ShopBucketName = "coffeepeek.shops"
    };

    [Fact]
    public void Roaster_MapsCoverPhoto_LowestSortIndexWins()
    {
        var mapper = MapsterConfiguration.CreateMapper(Media);
        var roaster = new Roaster("Coffee Circus");
        roaster.ReplacePhotos(
        [
            new RoasterPhoto("b.jpg", "image/jpeg", "roasters/b.jpg", 100, Guid.NewGuid(), sortIndex: 1),
            new RoasterPhoto("a.jpg", "image/jpeg", "roasters/a.jpg", 100, Guid.NewGuid(), sortIndex: 0),
        ]);

        var dto = mapper.Map<RoasterDto>(roaster);

        dto.PhotoUrl.Should().Be("https://media.coffeepeek.by/coffeepeek.shops/roasters/a.jpg");
    }

    [Fact]
    public void Roaster_NoPhotos_PhotoUrlIsNull()
    {
        var mapper = MapsterConfiguration.CreateMapper(Media);
        var roaster = new Roaster("Coffee Circus");

        var dto = mapper.Map<RoasterDto>(roaster);

        dto.PhotoUrl.Should().BeNull();
    }
}
