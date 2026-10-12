using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Entities;

namespace CoffeePeek.Moderation.Application.Features.Admin.Audit;

public static class ModerationAuditWriter
{
    public static async Task WriteAsync(
        IModerationAuditLogRepository repository,
        ModerationAuditEntityType entityType,
        Guid entityId,
        string entityName,
        ModerationAuditAction action,
        Guid moderatorUserId,
        string? comment,
        CancellationToken ct)
    {
        var entry = ModerationAuditLog.Create(
            entityType, entityId, entityName, action, moderatorUserId, comment);
        await repository.AddAsync(entry, ct);
    }
}
