using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class CheckInModerationDecidedHandler
{
    public static async Task Handle(CheckInModerationDecidedEvent message,
        IQueryCheckInRepository repository, IUnitOfWork unitOfWork, ICacheService cache, CancellationToken ct)
    {
        var checkIn = await repository.GetTrackedByIdAsync(message.CheckInId, ct);
        if (checkIn == null) return;
        var state = message.Status switch
        {
            ModerationStatus.Approved => Domain.Aggregates.CheckInAggregate.CheckInModerationState.Approved,
            ModerationStatus.Pending => Domain.Aggregates.CheckInAggregate.CheckInModerationState.Pending,
            ModerationStatus.Rejected => Domain.Aggregates.CheckInAggregate.CheckInModerationState.Rejected,
            _ => Domain.Aggregates.CheckInAggregate.CheckInModerationState.NotSubmitted
        };
        if (!checkIn.ApplyModerationDecision(message.ContentRevision, message.SubmissionId,
                message.DecisionSequence, state, message.RejectionReason, message.DecidedAtUtc)) return;
        // The independent concurrency token rejects an edit/hide that won after this aggregate was loaded.
        // Wolverine retries on a fresh context and rechecks revision, visibility and decision order.
        await unitOfWork.SaveChangesAsync(ct);
        await CheckInCacheInvalidator.InvalidateAsync(cache, checkIn.ShopId, checkIn.UserId, ct);
    }
}
