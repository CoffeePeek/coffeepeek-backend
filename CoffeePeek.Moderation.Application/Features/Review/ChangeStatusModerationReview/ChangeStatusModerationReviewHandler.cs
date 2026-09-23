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
using MapsterMapper;
using DomainModerationStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;

namespace CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;

public static class ChangeStatusModerationReviewHandler
{
    public static async Task<(UpdateEntityResponse<ModerationStatus>, object?)> Handle(
        ChangeStatusModerationReviewCommand command,
        IModerationReviewRepository repository,
        IModerationAuditLogRepository auditLogRepository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var review = await repository.GetById(command.ModerationReviewId, ct);

        if (review == null)
            throw new NotFoundException("Moderation review not found");

        object? outboundEvent = null;

        var oldStatus = review.ModerationStatus;
        string? auditComment = null;

        switch (command.ModerationStatus)
        {
            case ModerationStatus.Approved:
                // Re-approve is a no-op: skip event + audit so consumers stay idempotent.
                if (review.Approve(command.UserId))
                {
                    var dto = mapper.Map<ModerationReviewDto>(review);
                    outboundEvent = new ModerationReviewApprovedEvent(dto);
                    await ModerationAuditWriter.WriteAsync(
                        auditLogRepository,
                        ModerationAuditEntityType.Review,
                        review.Id,
                        review.Header ?? review.Id.ToString(),
                        ModerationAuditAction.Approved,
                        command.UserId,
                        null,
                        ct);
                }
                break;

            case ModerationStatus.Rejected:
            {
                var reason = !string.IsNullOrWhiteSpace(command.Comment)
                    ? command.Comment.Trim()
                    : command.RejectReason;
                review.Reject(reason!, command.UserId);
                auditComment = reason;
                await ModerationAuditWriter.WriteAsync(
                    auditLogRepository,
                    ModerationAuditEntityType.Review,
                    review.Id,
                    review.Header ?? review.Id.ToString(),
                    ModerationAuditAction.Rejected,
                    command.UserId,
                    auditComment,
                    ct);
                break;
            }

            case ModerationStatus.Pending:
                review.MoveToPending(command.UserId);
                await ModerationAuditWriter.WriteAsync(
                    auditLogRepository,
                    ModerationAuditEntityType.Review,
                    review.Id,
                    review.Header ?? review.Id.ToString(),
                    ModerationAuditAction.Pending,
                    command.UserId,
                    null,
                    ct);
                break;
        }

        // Un-approving must hide the published copy in Shops, otherwise a rejected review stays public.
        if (oldStatus == DomainModerationStatus.Approved && review.ModerationStatus != DomainModerationStatus.Approved)
            outboundEvent = new ModerationReviewEditedEvent(mapper.Map<ModerationReviewDto>(review));

        await unitOfWork.SaveChangesAsync(ct);
        
        var response = UpdateEntityResponse<ModerationStatus>.Success((ModerationStatus)review.ModerationStatus, oldEntity: (ModerationStatus)oldStatus);
        
        return (response, outboundEvent);
    }
}
