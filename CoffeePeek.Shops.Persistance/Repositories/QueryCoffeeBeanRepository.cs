using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCoffeeBeanRepository(ShopsDbContext dbContext) : IQueryCoffeeBeanRepository
{
    private readonly DbSet<CoffeeBean> _repository = dbContext.CoffeeBeans;
    
    public Task<CoffeeBean[]> GetAll(CancellationToken ct = default)
    {
        return _repository.AsNoTracking().ToArrayAsync(ct);
    }

    public async Task<IEnumerable<CoffeeBean>> GetByIds(List<Guid> ids, CancellationToken ct)
    {
        return await _repository
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);
    }
}

public class CoffeeBeanRepository(ShopsDbContext dbContext) : ICoffeeBeanRepository
{
    public Task<CoffeeBean?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.CoffeeBeans.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<CoffeeBean?> GetByNameAsync(string name, CancellationToken ct = default) =>
        dbContext.CoffeeBeans.FirstOrDefaultAsync(b => EF.Functions.ILike(b.Name, name), ct);

    public void Add(CoffeeBean bean) => dbContext.CoffeeBeans.Add(bean);

    public void Remove(CoffeeBean bean) => dbContext.CoffeeBeans.Remove(bean);
}