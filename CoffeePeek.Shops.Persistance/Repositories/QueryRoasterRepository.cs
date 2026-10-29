using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryRoasterRepository(ShopsDbContext dbContext) : IQueryRoasterRepository
{
    private readonly DbSet<Roaster> _repository = dbContext.Roasters;

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return _repository.AnyAsync(r => r.Name.ToLower() == normalized, ct);
    }
    
    public Task<Roaster[]> GetAll()
    {
        return _repository
            .AsNoTracking()
            .Include(r => r.Photos)
            .OrderByDescending(r => r.CoffeeShops.Count(s => !s.IsHidden))
            .ThenBy(r => r.Name)
            .ToArrayAsync();
    }

    public async Task<IEnumerable<Roaster>> GetByIds(List<Guid> ids, CancellationToken ct)
    {
        return await _repository
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);
    }

    public Task<Roaster?> GetByIdWithShopsAndPhotosAsync(Guid id, CancellationToken ct = default) =>
        _repository
            .AsNoTracking()
            .Include(r => r.CoffeeShops)
                .ThenInclude(s => s.ShopPhotos)
            .Include(r => r.Photos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Guid?> GetIdByModerationId(Guid id, CancellationToken ct = default) =>
        _repository
            .AsNoTracking()
            .Where(r => r.ModerationId == id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<(Roaster[] Items, int TotalCount)> GetByOwnerAsync(
        Guid ownerUserId, int page, int pageSize, CancellationToken ct)
    {
        var query = _repository.AsNoTracking().Where(r => r.OwnerUserId == ownerUserId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(r => r.Photos).OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return (items, total);
    }
}
