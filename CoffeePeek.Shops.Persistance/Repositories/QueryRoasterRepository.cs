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
}

public class RoasterRepository(ShopsDbContext dbContext) : IRoasterRepository
{
    public Task<Roaster?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Roasters
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Roaster?> GetByNameAsync(string name, CancellationToken ct = default) =>
        dbContext.Roasters.FirstOrDefaultAsync(r => EF.Functions.ILike(r.Name, name), ct);

    public void Add(Roaster roaster) => dbContext.Roasters.Add(roaster);

    public void Remove(Roaster roaster) => dbContext.Roasters.Remove(roaster);
}
