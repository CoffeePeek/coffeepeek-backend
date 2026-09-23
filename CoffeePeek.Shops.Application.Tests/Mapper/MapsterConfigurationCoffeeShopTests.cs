using System;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Mapster;

namespace CoffeePeek.Shops.Application.Tests.Mapper;

public class MapsterConfigurationCoffeeShopTests
{
    [Fact]
    public void Adapt_CoffeeShopToPublicDtos_MapsDataCompletenessScore()
    {
        var config = MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions());
        var shop = new CoffeeShop(
            Guid.NewGuid(),
            "Complete Shop",
            null,
            PriceRange.Moderate,
            Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Test address", 53.9m, 27.56m);

        typeof(CoffeeShop)
            .GetProperty(nameof(CoffeeShop.DataCompletenessScore))!
            .SetValue(shop, (short)65);

        shop.Adapt<ShortShopDto>(config).DataCompletenessScore.Should().Be(65);
        shop.Adapt<CoffeeShopDetailsDto>(config).DataCompletenessScore.Should().Be(65);
        shop.Adapt<ShopDto>(config).DataCompletenessScore.Should().Be(65);
    }
}
