using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Roaster.UpdateModerationRoasterStatus;

public static class UpdateModerationRoasterStatusHandler
{
    public static async Task<(Response, object?)> Handle(
        UpdateModerationRoasterStatusCommand command,
        IModerationRoasterRepository repository,
        IModerationAuditLogRepository auditLogRepository,
        IMapper mapper,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return (Response.Error(404, "Roaster not found"), null);

        object? outboundEvent = null;
        string? auditComment = null;
        ModerationAuditAction? auditAction = null;

        if (command.ModerationStatus == ModerationStatus.Approved)
        {
            if (roaster.Approve())
            {
                auditAction = ModerationAuditAction.Approved;
                outboundEvent = new ModerationRoasterApprovedEvent(
                    roaster.UserId,
                    mapper.Map<ModerationRoasterDto>(roaster));
            }
        }
        else if (command.ModerationStatus == ModerationStatus.Rejected)
        {
            var rejectReason = string.IsNullOrWhiteSpace(command.Comment)
                ? "Rejected by moderator"
                : command.Comment.Trim();
            roaster.Reject(rejectReason);
            auditAction = ModerationAuditAction.Rejected;
            auditComment = rejectReason;
        }
        else
        {
            return (Response.Error(400, "Moderation status can only be set to Approved or Rejected"), null);
        }

        if (auditAction.HasValue)
        {
            await ModerationAuditWriter.WriteAsync(
                auditLogRepository,
                ModerationAuditEntityType.Roaster,
                roaster.Id,
                roaster.Name,
                auditAction.Value,
                command.UserId,
                auditComment,
                ct);
        }

        return (Response.Success(), outboundEvent);
    }
}
