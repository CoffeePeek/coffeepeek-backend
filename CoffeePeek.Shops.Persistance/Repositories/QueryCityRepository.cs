using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCityRepository(ShopsDbContext dbContext) : IQueryCityRepository
{
    private readonly DbSet<City> _repository = dbContext.Cities;
    
    public Task<City[]> GetAll(CancellationToken ct = default)
    {
        return _repository.AsNoTracking().ToArrayAsync(ct);
    }

    public Task<bool> Exists(Guid cityId, CancellationToken ct = default) =>
        _repository.AsNoTracking().AnyAsync(c => c.Id == cityId, ct);

    public Task<City?> GetByName(string name, CancellationToken ct = default) =>
        _repository.AsNoTracking()
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, name), ct);
}

public class CityRepository(ShopsDbContext dbContext) : ICityRepository
{
    public Task<City?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Cities.FirstOrDefaultAsync(c => c.Id == id, ct);

    public void Add(City city) => dbContext.Cities.Add(city);

    public void Remove(City city) => dbContext.Cities.Remove(city);
}