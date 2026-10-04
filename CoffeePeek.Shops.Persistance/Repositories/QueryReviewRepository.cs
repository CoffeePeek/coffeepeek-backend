using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryReviewRepository(ShopsDbContext dbContext) : IQueryReviewRepository
{
    private readonly DbSet<Review> _repository = dbContext.Reviews;
    
    public Task<Guid?> GetExistingReviewIdByUserAndShopAsync(Guid userId, Guid shopId, CancellationToken ct) =>
        _repository.AsNoTracking()
            .Where(r => r.UserId == userId && r.CoffeeShopId == shopId && !r.IsSoftDelete && !r.IsRemovedByAdmin)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<Review?> GetById(Guid reviewId, CancellationToken ct)
    {
        return await _repository.AsNoTracking().Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == reviewId && !x.IsSoftDelete, ct);
    }

    public async Task<HashSet<Guid>> GetHelpfulReviewIds(
        Guid userId,
        IReadOnlyCollection<Guid> reviewIds,
        CancellationToken ct)
    {
        if (reviewIds.Count == 0)
            return [];

        var ids = await dbContext.ReviewHelpfulVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && reviewIds.Contains(v.ReviewId))
            .Select(v => v.ReviewId)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }
    
        public async Task<(IReadOnlyList<Review> reviews, int totalCount)> GetByCoffeeShopId(
        Guid coffeeShopId, 
        int page = 1, 
        int pageSize = 10, 
        CancellationToken ct = default)
    {
        var query = _repository
            .AsNoTracking()
            .Where(r => r.CoffeeShopId == coffeeShopId && !r.IsSoftDelete)
            .OrderByDescending(r => r.CreatedAtUtc);

        var totalCount = await query.CountAsync(ct);
        
        var reviews = await query
            .Include(r => r.Photos)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (reviews, totalCount);
    }

    public async Task<(decimal averageRating, int count)> GetReviewStatsByCoffeeShopId(Guid coffeeShopId, CancellationToken ct)
    {
        // Aggregate over all visible reviews. Previously this took FirstOrDefault of a per-row projection with
        // integer division, so the shop page showed one arbitrary, truncated review score and "1 review".
        var stats = await _repository
            .AsNoTracking()
            .Where(r => r.CoffeeShopId == coffeeShopId && !r.IsSoftDelete)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                AverageRating = g.Average(r => (r.Rating.Place + r.Rating.Coffee + r.Rating.Service) / 3m),
                Count = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        return stats != null ? (Math.Round(stats.AverageRating, 2), stats.Count) : (0m, 0);
    }
}
