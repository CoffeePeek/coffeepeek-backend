using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class QueryModerationRoasterRepository(ModerationDbContext dbContext) : IQueryModerationRoasterRepository
{
    private readonly DbSet<ModerationRoaster> _repository = dbContext.ModerationRoasters;

    public Task<ModerationRoaster?> GetById(Guid id, CancellationToken ct = default) =>
        BuildReviewQuery().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(IReadOnlyList<ModerationRoaster> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        CancellationToken ct = default)
    {
        var query = BuildReviewQuery();

        if (status.HasValue)
            query = query.Where(r => r.ModerationStatus == status.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private IQueryable<ModerationRoaster> BuildReviewQuery() =>
        _repository.AsNoTracking()
            .Include(r => r.Contact)
            .Include(r => r.Location)
            .Include(r => r.Photos);
}
