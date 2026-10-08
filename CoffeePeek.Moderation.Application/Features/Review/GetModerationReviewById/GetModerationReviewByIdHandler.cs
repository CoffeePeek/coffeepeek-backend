using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Review.GetModerationReviewById;

public static class GetModerationReviewByIdHandler
{
    public static async Task<Response<ModerationReviewDto>> Handle(
        GetModerationReviewByIdQuery query,
        IQueryModerationReviewRepository reviewRepository,
        IMapper mapper,
        CancellationToken ct)
    {
        var review = await reviewRepository.GetById(query.Id, ct);
        if (review is null)
            return Response<ModerationReviewDto>.Error("Check-in moderation submission not found");

        return Response<ModerationReviewDto>.Success(mapper.Map<ModerationReviewDto>(review));
    }
}
