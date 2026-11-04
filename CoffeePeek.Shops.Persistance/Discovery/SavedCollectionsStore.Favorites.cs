using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class SavedCollectionsStore
{
    private async Task SyncFavoritesAsync(Guid owner, IEnumerable<(string Kind, Guid EntityId)> changed, CancellationToken ct)
    {
        var keys = changed.Distinct().ToHashSet();
        if (keys.Count == 0) return;
        var ids = keys.Select(k => k.EntityId).Distinct().ToArray();
        var members = await (from c in db.SavedCollections
            join i in db.Set<SavedCollectionItem>() on c.Id equals i.CollectionId
            where c.OwnerId == owner && c.RevokedAtUtc == null && ids.Contains(i.EntityId)
            select new { i.Kind, i.EntityId }).Distinct().ToArrayAsync(ct);
        var active = members.Select(i => (i.Kind, i.EntityId)).ToHashSet();
        var favorites = await db.DiscoveryFavorites.Where(f => f.UserId == owner && ids.Contains(f.EntityId)).ToArrayAsync(ct);
        var existing = favorites.ToDictionary(f => (f.Kind, f.EntityId));
        foreach (var key in keys)
        {
            var has = existing.TryGetValue(key, out var favorite);
            if (active.Contains(key) && !has) db.DiscoveryFavorites.Add(new(owner, key.Kind, key.EntityId));
            if (!active.Contains(key) && has) db.DiscoveryFavorites.Remove(favorite!);
        }
        await FlushAsync(ct);
    }
}
