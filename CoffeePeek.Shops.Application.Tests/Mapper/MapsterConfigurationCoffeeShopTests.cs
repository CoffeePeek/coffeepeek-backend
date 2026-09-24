using System;
using System.Linq;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
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
        AdminPublishedShopMapper.Map(shop, new MediaPublicUrlOptions()).DataCompletenessScore.Should().Be(65);
    }

    [Fact]
    public void Adapt_ReviewToReviewDto_PhotosCarryFullUrlInSortOrder()
    {
        // Regression: review photos had no fullUrl, so clients guessed the bucket (coffeepeek.reviews -> 403).
        var config = MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.coffeepeek.by",
            ShopBucketName = "coffeepeek.shops"
        });
        var owner = Guid.NewGuid();
        var review = Review.Create(Guid.NewGuid(), owner, "user", "Nice header", "A long enough review comment",
            4, 4, 4, moderationReviewId: Guid.NewGuid());
        review.ReplacePhotos([
            new ShopPhoto("b.jpg", "image/jpeg", "b.jpg", 20, owner, 1),
            new ShopPhoto("a.jpg", "image/jpeg", "a.jpg", 10, owner, 0)
        ]);

        var dto = review.Adapt<ReviewDto>(config);

        dto.Photos.Select(p => p.FullUrl).Should().Equal(
            "https://media.coffeepeek.by/coffeepeek.shops/a.jpg",
            "https://media.coffeepeek.by/coffeepeek.shops/b.jpg");
        dto.Photos.First().Size.Should().Be(10);
    }
}
