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
    public static async Task<ReviewAddedEvent?> Handle(
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

        // Moderation sends SortIndex = 0 for every photo, so the array position is the order
        // (same approach as CreateShopFromModerationService).
        var photos = reviewDto.Photos
            .Select((p, i) => new ShopPhoto(p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.OwnerId, i))
            .ToList();

        Guid? addedReviewId = null;
        if (existing != null)
        {
            existing.UpdateContent(
                reviewDto.Header,
                reviewDto.Comment,
                Rating.Create(reviewDto.Rating.Place, reviewDto.Rating.Service, reviewDto.Rating.Coffee));
            existing.Restore(); // un-hide a review that was hidden by an edit

            var removed = existing.ReplacePhotos(photos);
            reviewRepository.TrackPhotoReplacement(removed, photos);
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

            // Add() cascades the whole graph as Added, so no TrackPhotoReplacement is needed here.
            review.ReplacePhotos(photos);
            reviewRepository.Add(review);
            addedReviewId = review.Id;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(reviewDto.ShopId));
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        // Only a first-time publish counts towards the author's statistics; re-approval after an edit must not.
        return addedReviewId is { } id ? new ReviewAddedEvent(reviewDto.UserId, reviewDto.ShopId, id) : null;
    }
}
