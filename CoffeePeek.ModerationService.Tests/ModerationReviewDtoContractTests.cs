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
    public void SnapshotWithPhoto_ExposesCanonicalContentAndProtectedUrl()
    {
        var author = Guid.NewGuid();
        var checkInId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var photo = PhotoMetadata.CreateCheckInPhoto(photoId, 0, "checkin.jpg", "image/jpeg", "private-key", 1024, author);
        var review = ModerationReview.CreateCheckInSnapshot(checkInId, 2, author, Guid.NewGuid(),
            "Test User", "A detailed check-in comment.", 5, 4, 3, null, DateTime.UtcNow, [photo]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.com",
            ShopBucketName = "coffeepeek.shops"
        });

        var dto = mapper.Map<ModerationReviewDto>(review);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Photos.Should().ContainSingle();
        dto.CheckInId.Should().Be(checkInId);
        dto.ContentRevision.Should().Be(2);
        dto.Text.Should().Be(review.Comment);
        dto.VisitedAtUtc.Should().Be(review.VisitedAtUtc);
        dto.Photos[0].FileName.Should().Be("checkin.jpg");
        dto.Photos[0].FullUrl.Should().Be($"/api/v1/check-ins/{checkInId}/moderation-photos/{photoId}?revision=2");
        json.Should().Contain("\"photos\":[{");
        json.Should().Contain($"\"fullUrl\":\"/api/v1/check-ins/{checkInId}/moderation-photos/{photoId}?revision=2\"");
        json.Should().NotContain("\"header\"").And.NotContain("\"comment\"");
    }

    [Fact]
    public void SnapshotWithPhoto_WithImageProxy_StillExposesOnlyProtectedUrl()
    {
        var author = Guid.NewGuid();
        var checkInId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var photo = PhotoMetadata.CreateCheckInPhoto(photoId, 0, "checkin.jpg", "image/jpeg", "private-key", 1024, author);
        var snapshot = ModerationReview.CreateCheckInSnapshot(checkInId, 2, author, Guid.NewGuid(),
            "Test User", "A detailed check-in comment.", 5, 4, 3, null, DateTime.UtcNow, [photo]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.com",
            ImageProxyEndpoint = "https://media.example.com/img",
            ShopBucketName = "coffeepeek.shops"
        });

        var dto = mapper.Map<ModerationReviewDto>(snapshot);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Photos.Should().ContainSingle();
        dto.Photos[0].FullUrl.Should().Be($"/api/v1/check-ins/{checkInId}/moderation-photos/{photoId}?revision=2");
        dto.Photos[0].Urls.Should().BeNull();
        json.Should().NotContain("https://media.example.com");
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
