using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCoffeeDrinkRepository(ShopsDbContext dbContext) : ICoffeeDrinkRepository, IDrinkCatalog
{
    public Task<CoffeeDrinkDefinition[]> GetAllAsync(CancellationToken ct) =>
        dbContext.CoffeeDrinkDefinitions.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Slug).ToArrayAsync(ct);

    public Task<CoffeeDrinkDefinition?> FindTrackedAsync(string slug, CancellationToken ct) =>
        dbContext.CoffeeDrinkDefinitions.SingleOrDefaultAsync(d => d.Slug == slug, ct);

    public async Task<DrinkCatalogEntry?> FindAsync(string slug, CancellationToken ct) =>
        await dbContext.CoffeeDrinkDefinitions.AsNoTracking().Where(d => d.Slug == slug)
            .Select(d => new DrinkCatalogEntry(d.NameRu, d.NameEn, d.IsActive)).SingleOrDefaultAsync(ct);

    public void Add(CoffeeDrinkDefinition drink) => dbContext.CoffeeDrinkDefinitions.Add(drink);

    public Task<CoffeeDrinkDefinition[]> GetActiveAsync(CancellationToken ct = default) =>
        dbContext.CoffeeDrinkDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.NameEn)
            .ToArrayAsync(ct);
}
