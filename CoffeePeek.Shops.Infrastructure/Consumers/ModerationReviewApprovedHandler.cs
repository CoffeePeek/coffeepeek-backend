using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class ModerationReviewApprovedHandler
{
    public static async Task<ReviewAddedEvent> Handle(
        ModerationReviewApprovedEvent @event,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var reviewDto = @event.Review;

        // Upsert by the source moderation review id. The previous AnyAsync(shopId, userId)
        // skip-if-exists silently dropped EVERY re-approval — that was the core bug.
        var existing = await reviewRepository.GetByModerationReviewId(reviewDto.Id, ct);

        Guid reviewId;
        if (existing != null)
        {
            existing.UpdateContent(
                reviewDto.Header,
                reviewDto.Comment,
                Rating.Create(reviewDto.Rating.Place, reviewDto.Rating.Service, reviewDto.Rating.Coffee));
            existing.Restore(); // un-hide a review that was hidden by an edit
            reviewId = existing.Id;
        }
        else
        {
            var review = Review.Create(
                reviewDto.ShopId,
                reviewDto.UserId,
                reviewDto.UserName,
                reviewDto.Header,
                reviewDto.Comment,
                reviewDto.Rating.Place, reviewDto.Rating.Service, reviewDto.Rating.Coffee,
                moderationReviewId: reviewDto.Id);

            reviewRepository.Add(review);
            reviewId = review.Id;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return new ReviewAddedEvent(reviewDto.UserId, reviewDto.ShopId, reviewId);
    }
}
