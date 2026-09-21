using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class ModerationReviewEditedHandler
{
    public static async Task Handle(
        ModerationReviewEditedEvent @event,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        // Editing sends the review back to Pending; hide the published copy until re-approval.
        var review = await reviewRepository.GetByModerationReviewId(@event.Review.Id, ct);

        if (review == null)
        {
            // Never published or a legacy unlinked row — nothing to hide.
            return;
        }

        review.Hide();

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId));
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}
