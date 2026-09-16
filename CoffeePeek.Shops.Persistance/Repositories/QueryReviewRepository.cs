using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryReviewRepository(ShopsDbContext dbContext) : IQueryReviewRepository
{
    private readonly DbSet<Review> _repository = dbContext.Reviews;
    
    public async Task<Review?> GetById(Guid reviewId, CancellationToken ct)
    {
        return await _repository.AsNoTracking().Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == reviewId, ct);
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
        var stats = await _repository
            .AsNoTracking()
            .Where(r => r.CoffeeShopId == coffeeShopId && !r.IsSoftDelete)
            .Select(g => new
            {
                AverageRating = (g.Rating.Place + g.Rating.Coffee + g.Rating.Service) / 3,
                Count = 1
            })
            .FirstOrDefaultAsync(ct);

        return stats != null ? (stats.AverageRating, stats.Count) : (0m, 0);
    }
}
