using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCoffeeDrinkRepository(ShopsDbContext dbContext) : IQueryCoffeeDrinkRepository
{
    public Task<CoffeeDrinkDefinition[]> GetActiveAsync(CancellationToken ct = default) =>
        dbContext.CoffeeDrinkDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.NameEn)
            .ToArrayAsync(ct);
}

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

public class ShopMenuRepository(ShopsDbContext dbContext) : IShopMenuRepository
{
    public Task<ShopMenu?> GetTrackedByShopIdAsync(Guid shopId, CancellationToken ct = default) =>
        dbContext.ShopMenus
            .Include(m => m.Items)
            .Include(m => m.Photos)
            .FirstOrDefaultAsync(m => m.CoffeeShopId == shopId, ct);

    public async Task<ShopMenu> ApplyManualItemsAsync(
        Guid shopId,
        IReadOnlyList<ManualShopMenuItemUpdate> items,
        Guid? userId,
        CancellationToken ct = default)
    {
        var menu = await dbContext.ShopMenus
            .FirstOrDefaultAsync(m => m.CoffeeShopId == shopId, ct);

        if (menu is null)
        {
            menu = ShopMenu.Create(shopId);
            foreach (var item in DistinctUpdates(items))
            {
                menu.ApplyManualItem(
                    item.DrinkDefinitionId,
                    item.Availability,
                    item.Price,
                    item.VolumeMl,
                    userId);
            }

            dbContext.ShopMenus.Add(menu);
            return menu;
        }

        menu.MarkManualUpdate(userId);
        var now = DateTime.UtcNow;
        foreach (var item in DistinctUpdates(items))
        {
            var itemId = Guid.NewGuid();
            var availability = (int)item.Availability;
            var price = item.Availability == MenuItemAvailability.Present ? item.Price : null;
            var source = (int)MenuItemSource.Manual;
            var kind = (int)CoffeeDrinkKind.Standard;

            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ShopMenuItems"
                    ("Id", "ShopMenuId", "DrinkDefinitionId", "Availability", "Price", "VolumeMl",
                     "Source", "Kind", "CustomName", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({itemId}, {menu.Id}, {item.DrinkDefinitionId}, {availability}, {price}, {item.VolumeMl},
                     {source}, {kind}, NULL, {now}, {now})
                ON CONFLICT ("ShopMenuId", "DrinkDefinitionId") DO UPDATE SET
                    "Availability" = EXCLUDED."Availability",
                    "Price" = EXCLUDED."Price",
                    "VolumeMl" = EXCLUDED."VolumeMl",
                    "Source" = EXCLUDED."Source",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                """, ct);
        }

        return menu;
    }

    public void Add(ShopMenu menu) => dbContext.ShopMenus.Add(menu);

    private static IEnumerable<ManualShopMenuItemUpdate> DistinctUpdates(
        IReadOnlyList<ManualShopMenuItemUpdate> items) =>
        items.GroupBy(item => item.DrinkDefinitionId).Select(group => group.Last());
}
