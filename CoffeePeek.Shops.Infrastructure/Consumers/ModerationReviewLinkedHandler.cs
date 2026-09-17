using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class ModerationReviewLinkedHandler
{
    public static async Task Handle(
        ModerationReviewLinkedEvent @event,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var review = await reviewRepository.GetUnlinkedByShopAndUser(@event.ShopId, @event.UserId, ct);

        if (review == null)
        {
            // Already linked or no matching published row — idempotent no-op.
            return;
        }

        review.LinkToModerationReview(@event.ModerationReviewId);

        // No cache invalidation: linking does not change public content.
        await unitOfWork.SaveChangesAsync(ct);
    }
}
