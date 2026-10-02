using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class ReviewReportRepository(ShopsDbContext db) : IReviewReportRepository
{
    public void Add(ReviewReport report) => db.ReviewReports.Add(report);

    public Task<ReviewReport?> GetByIdAsync(Guid reportId, CancellationToken ct) =>
        db.ReviewReports.FirstOrDefaultAsync(x => x.Id == reportId, ct);

    public async Task<(ReviewReport[] Items, int TotalCount)> GetPageAsync(
        ReviewReportStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ReviewReports.AsNoTracking();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return (items, count);
    }
}
