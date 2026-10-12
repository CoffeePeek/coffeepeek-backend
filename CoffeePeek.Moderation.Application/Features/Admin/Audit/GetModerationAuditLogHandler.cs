using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Admin.Audit;

public static class GetModerationAuditLogHandler
{
    public static async Task<Response<GetModerationAuditLogResponse>> Handle(
        GetModerationAuditLogQuery query,
        IModerationAuditLogRepository repository,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await repository.GetPagedAsync(
            page, pageSize, query.EntityType, query.Action, ct);

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var dtos = items.Select(x => new ModerationAuditLogDto(
            x.Id,
            x.EntityType,
            x.EntityId,
            x.EntityName,
            x.Action,
            x.ModeratorUserId,
            x.Comment,
            x.CreatedAtUtc)).ToList();

        return Response<GetModerationAuditLogResponse>.Success(new GetModerationAuditLogResponse(
            dtos, totalCount, totalPages, page, pageSize));
    }
}
