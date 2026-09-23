using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCheckInRepository(ShopsDbContext dbContext) : IQueryCheckInRepository
{
    private readonly DbSet<CheckIn> _repository = dbContext.CheckIns;
    
    public Task<bool> Exists(Guid userId, Guid coffeeShopId, CancellationToken ct = default)
    {
        return _repository.AnyAsync(x => x.UserId == userId && x.ShopId == coffeeShopId, ct);
    }

    public Task LockUserForCheckInAsync(Guid userId, CancellationToken ct = default) =>
        // Transaction-scoped: released on commit/rollback of the Wolverine handler transaction.
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"checkin:" + userId}, 0))", ct);

    public Task<bool> ExistsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        return _repository.AnyAsync(x => x.UserId == userId && x.CreatedAtUtc >= sinceUtc, ct);
    }

    public Task<int> CountSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        return _repository.CountAsync(x => x.UserId == userId && x.CreatedAtUtc >= sinceUtc, ct);
    }

    public async Task<IReadOnlyList<CheckIn>> GetByUserAndShopAsync(
        Guid userId,
        Guid shopId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        return await _repository
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.ShopId == shopId)
            .Include(x => x.ShopPhotos)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public void Add(CheckIn checkIn)
    {
        _repository.Add(checkIn);
    }

    public Task<List<Guid>> GetVisitedShopIdsAsync(Guid userId, CancellationToken ct = default)
    {
        return _repository
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.ShopId)
            .ToListAsync(ct);
    }

    public Task<int> GetCheckInCountByCoffeeShopIdAsync(Guid coffeeShopId, CancellationToken ct = default)
    {
        return _repository
            .AsNoTracking()
            .CountAsync(x => x.ShopId == coffeeShopId, ct);
    }

    public async Task<Dictionary<Guid, int>> GetCheckInCountsByShopIdsAsync(IEnumerable<Guid> shopIds, CancellationToken ct = default)
    {
        var shopIdList = shopIds.ToList();
        
        var counts = await _repository
            .AsNoTracking()
            .Where(x => shopIdList.Contains(x.ShopId))
            .GroupBy(x => x.ShopId)
            .Select(g => new { ShopId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(c => c.ShopId, c => c.Count);
    }

    public Task<bool> ExistsByIdAsync(Guid checkInId, CancellationToken ct = default) =>
        _repository.AsNoTracking().AnyAsync(x => x.Id == checkInId, ct);

    public Task<Guid?> GetUserIdByIdAsync(Guid checkInId, CancellationToken ct = default) =>
        _repository
            .AsNoTracking()
            .Where(x => x.Id == checkInId)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(ct);
}
