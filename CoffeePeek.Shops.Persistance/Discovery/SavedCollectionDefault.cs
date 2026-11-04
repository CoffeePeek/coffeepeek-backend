using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

internal static class SavedCollectionDefault
{
    internal const string Title = "Избранное";

    internal static async Task<SavedCollection> EnsureAsync(ShopsDbContext db, Guid owner, CancellationToken ct)
    {
        await LockAsync(db, owner, ct);
        var active = db.SavedCollections.Where(c => c.OwnerId == owner && c.RevokedAtUtc == null);
        var row = await active.Include(c => c.Items).SingleOrDefaultAsync(c => c.IsDefault, ct);
        if (row != null) return row;
        row = await active.Include(c => c.Items).Where(c => c.Title == Title)
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).FirstOrDefaultAsync(ct);
        if (row == null)
        {
            row = SavedCollection.CreateList(owner, Guid.NewGuid(), Title, null, "private");
            db.SavedCollections.Add(row);
        }
        row.MarkDefault();
        if (row.Visibility == "private") await SeedLegacyFavoritesAsync(db, owner, row, ct);
        return row;
    }

    private static async Task SeedLegacyFavoritesAsync(ShopsDbContext db, Guid owner, SavedCollection row, CancellationToken ct)
    {
        var legacy = await db.DiscoveryFavorites.AsNoTracking().Where(f => f.UserId == owner && f.Kind != "drink").ToArrayAsync(ct);
        var existing = await (from c in db.SavedCollections.AsNoTracking()
            join i in db.Set<SavedCollectionItem>().AsNoTracking() on c.Id equals i.CollectionId
            where c.OwnerId == owner && c.RevokedAtUtc == null
            select new { i.Kind, i.EntityId }).Distinct().ToArrayAsync(ct);
        var keys = existing.Select(i => (i.Kind, i.EntityId)).ToHashSet();
        var unmatched = legacy.Where(f => !keys.Contains((f.Kind, f.EntityId))).ToArray();
        var shopIds = unmatched.Where(f => f.Kind == "coffee_shop").Select(f => f.EntityId).ToArray();
        var roasterIds = unmatched.Where(f => f.Kind == "roaster").Select(f => f.EntityId).ToArray();
        var visibleShops = await db.Shops.Where(s => shopIds.Contains(s.Id) && s.Status == CoffeeShopStatus.Active &&
            !s.IsHidden && s.PublicSlug != null).Select(s => s.Id).ToArrayAsync(ct);
        var visibleRoasters = await db.Roasters.Where(r => roasterIds.Contains(r.Id) && r.PublicSlug != null)
            .Select(r => r.Id).ToArrayAsync(ct);
        var selected = unmatched.Where(f => f.Kind == "coffee_shop" ? visibleShops.Contains(f.EntityId) :
            visibleRoasters.Contains(f.EntityId)).ToArray();
        if (selected.Length == 0) return;
        row.ReplaceItems(row.Items.OrderBy(i => i.Position).Select(i => new DiscoveryFavorite(owner, i.Kind, i.EntityId))
            .Concat(selected));
    }

    internal static async Task LockAsync(ShopsDbContext db, Guid owner, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Collection writes require the owning transaction.");
        var key = "saved-collections:" + owner.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }
}
