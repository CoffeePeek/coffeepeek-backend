using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Response;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public static class DecideShopChangeRequestHandler
{
    public static async Task<(Response, object?)> Handle(
        DecideShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IModerationAuditLogRepository auditRepository,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(command.RequestId, ct);
        if (request is null) return (Response.Error(404, "Change request not found."), null);

        object? outbound = null;
        if (command.Status == ModerationStatus.Approved)
        {
            var now = DateTime.UtcNow;
            if (request.Approve(command.ModeratorUserId, now))
            {
                outbound = new ShopChangeRequestApprovedV1(
                    request.Id, request.ShopId, request.SubmittedByUserId,
                    (ShopChangeSection)request.Section,
                    ShopChangeRequestPayload.Deserialize(request.PayloadJson),
                    command.ModeratorUserId, now);
                await ModerationAuditWriter.WriteAsync(
                    auditRepository, ModerationAuditEntityType.ShopChangeRequest, request.Id,
                    $"{request.ShopId}:{request.Section}", ModerationAuditAction.Approved,
                    command.ModeratorUserId, null, ct);
            }
        }
        else if (command.Status == ModerationStatus.Rejected)
        {
            request.Reject(command.ModeratorUserId, command.Comment ?? string.Empty, DateTime.UtcNow);
            await ModerationAuditWriter.WriteAsync(
                auditRepository, ModerationAuditEntityType.ShopChangeRequest, request.Id,
                $"{request.ShopId}:{request.Section}", ModerationAuditAction.Rejected,
                command.ModeratorUserId, command.Comment, ct);
        }
        else
        {
            return (Response.Error(400, "Status can only be Approved or Rejected."), null);
        }

        return (Response.Success(), outbound);
    }
}
