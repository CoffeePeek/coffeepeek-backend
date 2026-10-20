using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using Wolverine;

namespace CoffeePeek.Moderation.Application.Features.Shop.UpdateModerationShopStatus;

public static class UpdateModerationCoffeeShopStatusHandler
{
    public static async Task<(Response, object?)> Handle(
        UpdateModerationCoffeeShopStatusCommand command,
        IModerationShopRepository repository,
        IModerationAuditLogRepository auditLogRepository,
        IMapper mapper,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.Id, ct);

        if (shop == null)
            return (Response.Error(404, "CoffeeShop not found"), null);

        object? outboundEvent = null;
        string? auditComment = null;
        ModerationAuditAction? auditAction = null;

        if (command.ModerationStatus == ModerationStatus.Approved)
        {
            // Re-approve is a no-op: skip event + audit so consumers stay idempotent.
            if (shop.Approve())
            {
                auditAction = ModerationAuditAction.Approved;
                outboundEvent = new ModerationShopApprovedEvent(
                    shop.UserId,
                    mapper.Map<ShopDto>(shop));
            }
        }
        else if (command.ModerationStatus == ModerationStatus.Rejected)
        {
            var rejectReason = string.IsNullOrWhiteSpace(command.Comment)
                ? "Rejected by moderator"
                : command.Comment.Trim();
            if (shop.Reject(rejectReason))
            {
                auditAction = ModerationAuditAction.Rejected;
                auditComment = shop.RejectedReason;
            }
        }
        else
        {
            return (Response.Error(400, "Moderation status can only be set to Approved or Rejected"), null);
        }

        if (auditAction.HasValue)
        {
            if (shop.UserId is { } recipient)
                await bus.PublishAsync(new ModerationDecisionChanged(Guid.NewGuid(), recipient,
                    ModerationSubmissionKind.Shop, shop.Id, shop.ModerationStatus.ToString()));
            await ModerationAuditWriter.WriteAsync(
                auditLogRepository,
                ModerationAuditEntityType.Shop,
                shop.Id,
                shop.Name,
                auditAction.Value,
                command.UserId,
                auditComment,
                ct);
        }

        return (Response.Success(), outboundEvent);
    }
}
