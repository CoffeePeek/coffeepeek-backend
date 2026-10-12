using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationAuditLogRepository(ModerationDbContext dbContext) : IModerationAuditLogRepository
{
    public Task AddAsync(ModerationAuditLog entry, CancellationToken ct = default)
    {
        dbContext.ModerationAuditLogs.Add(entry);
        return Task.CompletedTask;
    }

    public async Task<(IReadOnlyList<ModerationAuditLog> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ModerationAuditEntityType? entityType,
        ModerationAuditAction? action,
        CancellationToken ct = default)
    {
        var query = dbContext.ModerationAuditLogs.AsNoTracking();

        if (entityType.HasValue)
            query = query.Where(x => x.EntityType == entityType.Value);

        if (action.HasValue)
            query = query.Where(x => x.Action == action.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
