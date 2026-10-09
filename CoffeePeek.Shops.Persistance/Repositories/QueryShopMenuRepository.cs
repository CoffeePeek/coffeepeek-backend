using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryShopMenuRepository(ShopsDbContext dbContext) : IQueryShopMenuRepository
{
    public Task<ShopMenu?> GetByShopIdAsync(Guid shopId, CancellationToken ct = default) =>
        dbContext.ShopMenus
            .AsNoTracking()
            .Include(m => m.Items)
            .ThenInclude(i => i.DrinkDefinition)
            .Include(m => m.Photos)
            .FirstOrDefaultAsync(m => m.CoffeeShopId == shopId, ct);
}
