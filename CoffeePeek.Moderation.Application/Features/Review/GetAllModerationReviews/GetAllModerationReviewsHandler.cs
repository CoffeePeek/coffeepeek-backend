using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;

public static class GetAllModerationReviewsHandler
{
    public static async Task<Response<GetAllModerationReviewsResponse>> Handle(
        GetAllModerationReviewsQuery query,
        IQueryModerationReviewRepository reviewRepository,
        IMapper mapper,
        CancellationToken ct)
    {
        ModerationStatus? domainStatus = query.Status.HasValue
            ? (ModerationStatus?)query.Status.Value
            : null;

        var (items, totalCount) = await reviewRepository.GetPagedAsync(
            query.Page,
            query.PageSize,
            domainStatus,
            query.Search,
            cancellationToken: ct);

        var moderationReviewDtos = mapper.Map<ModerationReviewDto[]>(items);
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return Response<GetAllModerationReviewsResponse>.Success(new GetAllModerationReviewsResponse(
            moderationReviewDtos,
            totalCount,
            totalPages,
            query.Page,
            query.PageSize));
    }
}
