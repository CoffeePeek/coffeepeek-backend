using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

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
