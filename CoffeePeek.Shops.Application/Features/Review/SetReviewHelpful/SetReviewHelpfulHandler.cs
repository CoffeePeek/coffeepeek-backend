using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.SetReviewHelpful;

public class SetReviewHelpfulHandler
{
    public async Task<Response<SetReviewHelpfulResponse>> Handle(
        SetReviewHelpfulCommand request,
        IReviewRepository reviewRepository,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var review = await reviewRepository.GetById(request.ReviewId, ct);

        if (review is null)
            throw new NotFoundException($"{nameof(Domain.Aggregates.ReviewAggregate.Review)} not found by id");

        if (review.UserId == request.UserId)
            throw new ForbiddenException("You cannot mark your own review as helpful");

        var result = await reviewRepository.SetHelpfulAsync(
            request.ReviewId, request.UserId, request.IsHelpful, ct);

        // Only bust the cached shop detail when the persisted count actually moved.
        if (result.Changed)
            await cacheService.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId));

        return Response<SetReviewHelpfulResponse>.Success(
            new SetReviewHelpfulResponse(result.IsHelpful, result.HelpfulCount));
    }
}
