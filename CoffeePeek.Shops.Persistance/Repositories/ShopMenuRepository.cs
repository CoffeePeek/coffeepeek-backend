using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

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
        // Validate the full packet before any SQL or tracked mutation.
        ManualShopMenuItemUpdateValidator.Validate(items);

        var menu = await dbContext.ShopMenus
            .FirstOrDefaultAsync(m => m.CoffeeShopId == shopId, ct);

        if (menu is null)
        {
            menu = ShopMenu.Create(shopId);
            menu.ApplyManualItems(items, userId);

            dbContext.ShopMenus.Add(menu);
            return menu;
        }

        var trackedItems = dbContext.ChangeTracker.Entries<ShopMenuItem>()
            .Where(entry => entry.Entity.ShopMenuId == menu.Id)
            .ToArray();
        if (trackedItems.Any(entry => entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Manual SQL update cannot replace pending tracked menu item changes.");

        // Raw upserts and the revision bump must commit together. Reuse Wolverine's ambient
        // EF transaction; direct repository callers receive a short local transaction.
        await using var localTransaction = dbContext.Database.IsRelational() &&
                                           dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(ct)
            : null;
        var expectedRevision = menu.InputRevision;
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var itemId = Guid.NewGuid();
            var availability = (int)item.Availability;
            var price = item.Price;
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

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ShopMenus"
            SET "InputRevision" = "InputRevision" + 1,
                "UpdatedByUserId" = {userId},
                "ParseStatus" = CASE WHEN "ParseStatus" = 0 THEN 3 ELSE "ParseStatus" END,
                "UpdatedAtUtc" = {now}
            WHERE "Id" = {menu.Id} AND "InputRevision" = {expectedRevision}
            """, ct);
        if (updated != 1)
        {
            ShopsTelemetry.MenuWriteConflict();
            throw new ConflictException("Menu changed during manual update; reload and retry.", statusCode: 409);
        }

        await dbContext.Entry(menu).ReloadAsync(ct);

        // ExecuteSql bypasses EF's change tracker. Discard only the stale menu item entries,
        // then repopulate the navigation so callers can use the returned aggregate safely.
        foreach (var entry in trackedItems)
            entry.State = EntityState.Detached;
        var collection = dbContext.Entry(menu).Collection(m => m.Items);
        // Detaching an entity does not remove it from the aggregate's backing list.
        // Clear that list before EF fixes up the rows reloaded after the raw upsert.
        if (collection.Metadata.FieldInfo?.GetValue(menu) is not List<ShopMenuItem> backingItems)
            throw new InvalidOperationException("Shop menu item navigation has no mutable backing list.");
        backingItems.Clear();
        collection.IsLoaded = false;
        await collection.LoadAsync(ct);

        if (localTransaction is not null)
            await localTransaction.CommitAsync(ct);

        return menu;
    }

    public void Add(ShopMenu menu) => dbContext.ShopMenus.Add(menu);
}
