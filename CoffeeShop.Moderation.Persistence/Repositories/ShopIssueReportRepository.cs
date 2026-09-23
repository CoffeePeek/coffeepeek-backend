using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ShopIssueReportRepository(ModerationDbContext dbContext) : IShopIssueReportRepository
{
    private readonly DbSet<ShopIssueReport> _reports = dbContext.ShopIssueReports;

    public void Add(ShopIssueReport report)
    {
        _reports.Add(report);
    }

    public Task<ShopIssueReport?> GetById(Guid id, CancellationToken ct = default)
    {
        return _reports.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(IReadOnlyList<ShopIssueReport> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ShopIssueReportStatus? status,
        Guid? shopId,
        CancellationToken ct = default)
    {
        var query = _reports.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (shopId.HasValue)
            query = query.Where(x => x.ShopId == shopId.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
