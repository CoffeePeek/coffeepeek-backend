using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class SavedCollectionsStore
{
    public async Task<SavedCollectionSummary[]> SetMembershipAsync(Guid owner, string kind, string slug, Guid[] collectionIds, CancellationToken ct)
    {
        if (collectionIds == null || collectionIds.Any(id => id == Guid.Empty) || collectionIds.Distinct().Count() != collectionIds.Length)
            throw new DiscoveryException("Invalid collection selection.", 400, "INVALID_COLLECTION");
        await LockOwnerAsync(owner, ct);
        var target = await ResolveAsync(kind, slug, ct);
        if (target == Guid.Empty) throw new DiscoveryException("Place not found.", 404, "NOT_FOUND");
        var rows = await Lists(owner).Include(c => c.Items).ToArrayAsync(ct);
        if (collectionIds.Any(id => rows.All(c => c.Id != id)))
            throw new DiscoveryException("Collection not found.", 404, "NOT_FOUND");
        var additions = rows.Where(c => collectionIds.Contains(c.Id) && c.Items.All(i => i.Kind != kind || i.EntityId != target)).ToArray();
        if (additions.Length > 0) await EnsureFavoriteAsync(owner, kind, target, ct);
        foreach (var row in rows)
        {
            var has = row.Items.Any(i => i.Kind == kind && i.EntityId == target);
            var selected = collectionIds.Contains(row.Id);
            if (has == selected) continue;
            var items = row.Items.OrderBy(i => i.Position).Select(i => new DiscoveryFavorite(owner, i.Kind, i.EntityId)).ToList();
            if (selected) items.Add(new(owner, kind, target));
            else items.RemoveAll(i => i.Kind == kind && i.EntityId == target);
            row.ReplaceItems(items);
        }
        await FlushAsync(ct);
        return await ListAsync(owner, kind, slug, ct);
    }

    public async Task<FeaturedCollectionPreference> SetFeaturedAsync(Guid owner, Guid? id, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        if (id.HasValue && !await Lists(owner).AnyAsync(c => c.Id == id && c.Visibility == "public", ct))
            throw new DiscoveryException("Public owned collection not found.", 404, "NOT_FOUND");
        var preference = await db.SavedCollectionPreferences.FindAsync([owner], ct);
        if (preference == null) db.SavedCollectionPreferences.Add(preference = new(owner));
        preference.Select(id);
        await FlushAsync(ct);
        return new(id);
    }
}
