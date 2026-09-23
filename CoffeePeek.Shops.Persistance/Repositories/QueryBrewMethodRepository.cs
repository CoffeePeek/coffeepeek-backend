using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryBrewMethodRepository(ShopsDbContext dbContext) : IQueryBrewMethodRepository
{
    private readonly DbSet<BrewMethod> _repository = dbContext.BrewMethods;
    
    public Task<BrewMethod[]> GetAll(CancellationToken ct = default)
    {
        return _repository.AsNoTracking().ToArrayAsync(ct);
    }

    public async Task<IEnumerable<BrewMethod>> GetByIds(List<Guid> ids, CancellationToken ct)
    {
        return await _repository
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);
    }
}

public class BrewMethodRepository(ShopsDbContext dbContext) : IBrewMethodRepository
{
    public Task<BrewMethod?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.BrewMethods.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<BrewMethod?> GetByNameAsync(string name, CancellationToken ct = default) =>
        dbContext.BrewMethods.FirstOrDefaultAsync(b => EF.Functions.ILike(b.Name, name), ct);

    public void Add(BrewMethod brewMethod) => dbContext.BrewMethods.Add(brewMethod);

    public void Remove(BrewMethod brewMethod) => dbContext.BrewMethods.Remove(brewMethod);
}