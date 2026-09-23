using CoffeePeek.Contract.Dtos.CoffeeShop;
using Mapster;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.Moderation.Application.Mapper;

public partial class MapsterConfiguration
{
    private static void ConfigureModerationReview(TypeAdapterConfig config)
    {
        config.NewConfig<ModerationReview, ModerationReviewDto>()
            // DTO uses CreatedAt; entity audit field is CreatedAtUtc
            .Map(dest => dest.CreatedAt, src => src.CreatedAtUtc)
            .Map(dest => dest.Photos, src => src.ReviewPhotos);
    }
}
