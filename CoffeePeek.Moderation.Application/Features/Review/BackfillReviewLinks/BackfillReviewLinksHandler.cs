using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Response;
using Wolverine;

namespace CoffeePeek.Moderation.Application.Features.Review.BackfillReviewLinks;

public static class BackfillReviewLinksHandler
{
    public static async Task<Response<int>> Handle(
        BackfillReviewLinksCommand command,
        IQueryModerationReviewRepository queryRepository,
        IMessageBus bus,
        CancellationToken ct)
    {
        var all = await queryRepository.GetAll(ct);

        var count = 0;
        foreach (var review in all.Where(r => r.ModerationStatus == ModerationStatus.Approved))
        {
            // Idempotent: the Shops-side ModerationReviewLinkedHandler only links rows where
            // ModerationReviewId IS NULL, so re-running this backfill is safe.
            await bus.PublishAsync(new ModerationReviewLinkedEvent(review.ShopId, review.UserId, review.Id));
            count++;
        }

        return Response<int>.Success(count);
    }
}
