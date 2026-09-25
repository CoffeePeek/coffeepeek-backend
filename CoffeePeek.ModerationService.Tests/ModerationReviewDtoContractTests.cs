using System.Text.Json;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.ModerationService.Tests;

public class ModerationReviewDtoContractTests
{
    [Fact]
    public void ReviewWithPhoto_MappingAndSerialization_ExposePhotoWithFullUrl()
    {
        var photo = PhotoMetadata.Create(
            "review.jpg",
            "image/jpeg",
            "reviews/review.jpg",
            1_024,
            Guid.NewGuid(),
            null);
        var review = ModerationReview.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Test User",
            null,
            "A detailed review comment.",
            5,
            4,
            3,
            [photo]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.com",
            ShopBucketName = "coffeepeek.shops"
        });

        var dto = mapper.Map<ModerationReviewDto>(review);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Photos.Should().ContainSingle();
        dto.Photos[0].FileName.Should().Be("review.jpg");
        dto.Photos[0].FullUrl.Should().Be(
            "https://media.example.com/coffeepeek.shops/reviews/review.jpg");
        json.Should().Contain("\"photos\":[{");
        json.Should().Contain(
            "\"fullUrl\":\"https://media.example.com/coffeepeek.shops/reviews/review.jpg\"");
    }

    [Fact]
    public void ReviewWithPhoto_WithImageProxy_ExposesVariantUrls()
    {
        var photo = PhotoMetadata.Create(
            "review.jpg",
            "image/jpeg",
            "reviews/review.jpg",
            1_024,
            Guid.NewGuid(),
            null);
        var review = ModerationReview.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Test User",
            null,
            "A detailed review comment.",
            5,
            4,
            3,
            [photo]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.com",
            ImageProxyEndpoint = "https://media.example.com/img",
            ShopBucketName = "coffeepeek.shops"
        });

        var dto = mapper.Map<ModerationReviewDto>(review);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Photos.Should().ContainSingle();
        dto.Photos[0].FullUrl.Should().Be(
            "https://media.example.com/coffeepeek.shops/reviews/review.jpg");
        dto.Photos[0].Urls.Should().NotBeNull();
        dto.Photos[0].Urls!.Detail.Should().Be(
            "https://media.example.com/img/_/detail/plain/coffeepeek.shops/reviews/review.jpg");
        dto.Photos[0].Urls.Thumbnail.Should().Be(
            "https://media.example.com/img/_/thumb/plain/coffeepeek.shops/reviews/review.jpg");
        json.Should().Contain("\"urls\":{");
        json.Should().Contain("\"fullscreen\":");
    }

    [Fact]
    public void DefaultDto_WebSerialization_ExposesEmptyPhotosCollection()
    {
        var dto = new ModerationReviewDto();

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Photos.Should().BeEmpty();
        json.Should().Contain("\"photos\":[]");
    }
}
