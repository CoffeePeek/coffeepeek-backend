using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;

public static class ChangeStatusModerationReviewHandler
{
    public static async Task<(UpdateEntityResponse<ModerationStatus>, CheckInModerationDecidedEvent?)> Handle(
        ChangeStatusModerationReviewCommand command,
        IModerationReviewRepository repository,
        IModerationAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var review = await repository.GetById(command.SubmissionId, ct);

        if (review == null)
            throw new NotFoundException("Check-in moderation submission not found");

        if (command.PendingOnly && review.ModerationStatus != Domain.Common.Enums.ModerationStatus.Pending)
            return (UpdateEntityResponse<ModerationStatus>.Error("Submission already decided"), null);

        CheckInModerationDecidedEvent? outboundEvent = null;

        var oldStatus = review.ModerationStatus;
        string? auditComment = null;
        ModerationAuditAction auditAction;
        bool changed;

        switch (command.ModerationStatus)
        {
            case ModerationStatus.Approved:
                changed = review.Approve(command.UserId);
                auditAction = ModerationAuditAction.Approved;
                break;

            case ModerationStatus.Rejected:
            {
                var reason = !string.IsNullOrWhiteSpace(command.Comment)
                    ? command.Comment.Trim()
                    : command.RejectReason;
                changed = review.Reject(reason!, command.UserId);
                auditComment = reason;
                auditAction = ModerationAuditAction.Rejected;
                break;
            }

            case ModerationStatus.Pending:
                changed = review.MoveToPending(command.UserId);
                auditAction = ModerationAuditAction.Pending;
                break;

            default:
                throw new DomainException("Unknown moderation status.");
        }

        if (changed)
        {
            await ModerationAuditWriter.WriteAsync(auditLogRepository, ModerationAuditEntityType.Review,
                review.Id, review.CheckInId.ToString(), auditAction, command.UserId, auditComment, ct);
            outboundEvent = new CheckInModerationDecidedEvent(review.CheckInId,
                review.ContentRevision, review.Id, review.DecisionSequence, (ModerationStatus)review.ModerationStatus,
                review.RejectedReason, review.ModeratedAt ?? DateTime.UtcNow);

            // xmin rejects competing status writes; Wolverine retries in a fresh context.
            // The returned decision and this audit/status update share the durable EF outbox transaction.
            await unitOfWork.SaveChangesAsync(ct);
        }

        var response = UpdateEntityResponse<ModerationStatus>.Success((ModerationStatus)review.ModerationStatus, oldEntity: (ModerationStatus)oldStatus);

        return (response, outboundEvent);
    }
}
