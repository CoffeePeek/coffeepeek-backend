using CoffeePeek.Contract.Dtos.CoffeeShop;
using Mapster;
using CoffeePeek.Contract.Dtos;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.Moderation.Application.Mapper;

public partial class MapsterConfiguration
{
    private static void ConfigureModerationReview(TypeAdapterConfig config)
    {
        config.NewConfig<ModerationReview, ModerationReviewDto>()
            // DTO uses CreatedAt; entity audit field is CreatedAtUtc
            .Map(dest => dest.CreatedAt, src => src.CreatedAtUtc)
            .Map(dest => dest.Text, src => src.Comment)
            .Map(dest => dest.Photos, src => MapReviewPhotos(src));
    }

    private static PhotoMetadataDto[] MapReviewPhotos(ModerationReview review) =>
        review.ReviewPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.CreatedAtUtc).Select(p => new PhotoMetadataDto
        {
            Id = p.CheckInPhotoId ?? throw new InvalidOperationException("A snapshot photo requires canonical identity."),
            FileName = p.FileName,
            ContentType = p.ContentType,
            StorageKey = p.StorageKey,
            SizeBytes = p.SizeBytes,
            OwnerId = p.OwnerId ?? Guid.Empty,
            UploadedAt = p.CreatedAtUtc,
            SortIndex = p.SortIndex,
            FullUrl = $"/api/v1/check-ins/{review.CheckInId}/moderation-photos/{p.CheckInPhotoId}?revision={review.ContentRevision}"
        }).ToArray();
}
