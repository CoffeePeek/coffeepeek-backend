using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed class DiscoveryFavoritesStore(ShopsDbContext db, IPublicAddressService addresses,
    IOptions<MediaPublicUrlOptions>? media = null) : IDiscoveryFavoritesStore
{
    public async Task<DiscoveryFavoriteItem[]> ListAsync(Guid userId, CancellationToken ct)
    {
        var favorites = await db.DiscoveryFavorites.AsNoTracking().Where(f => f.UserId == userId).ToArrayAsync(ct);
        var members = await (from c in db.SavedCollections.AsNoTracking()
            join i in db.Set<SavedCollectionItem>().AsNoTracking() on c.Id equals i.CollectionId
            where c.OwnerId == userId && c.RevokedAtUtc == null
            select new { i.Kind, i.EntityId, c.CreatedAtUtc }).ToArrayAsync(ct);
        var keys = favorites.Select(f => (f.Kind, f.EntityId)).ToHashSet();
        var added = members.GroupBy(i => (i.Kind, i.EntityId)).Where(g => !keys.Contains(g.Key))
            .Select(g => new DiscoveryFavorite(userId, g.Key.Kind, g.Key.EntityId, g.Min(i => i.CreatedAtUtc)));
        var ordered = favorites.Concat(added).OrderByDescending(f => f.CreatedAtUtc)
            .ThenBy(f => f.Kind).ThenBy(f => f.EntityId).ToArray();
        return await DiscoveryFavoriteCards.ReadAsync(db, ordered, media, ct);
    }

    public async Task SetAsync(Guid userId, string kind, string slug, bool favorite, CancellationToken ct)
    {
        if (kind is not ("coffee_shop" or "roaster" or "drink")) throw new DiscoveryException("Unknown favorite kind.");
        if (kind == "drink")
        {
            // Catalog drink slugs are stable and may contain underscores; they are not public-address slugs.
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > CoffeePeek.Shops.Domain.BusinessConstants.MaxCoffeeDrinkSlugLength ||
                !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:[_-][a-z0-9]+)*$"))
                throw new DiscoveryException("Invalid drink slug.");
        }
        else if (!PublicSlug.IsValid(slug)) throw new DiscoveryException("Invalid public slug.");
        var addressKind = kind == "coffee_shop" ? PublicAddressKind.CoffeeShop : PublicAddressKind.Roaster;
        Guid id;
        if (kind == "drink") id = await db.CoffeeDrinkDefinitions.Where(d => d.Slug == slug && d.IsActive)
            .Select(d => d.Id).SingleOrDefaultAsync(ct);
        else if (db.PublicAddressesEnabled) id = (await addresses.ResolveAsync(addressKind, slug, ct))?.EntityId ?? Guid.Empty;
        else id = kind == "coffee_shop"
            ? await db.Shops.Where(s => s.PublicSlug == slug).Select(s => s.Id).SingleOrDefaultAsync(ct)
            : await db.Roasters.Where(r => r.PublicSlug == slug).Select(r => r.Id).SingleOrDefaultAsync(ct);
        var exists = kind switch
        {
            "coffee_shop" => await db.Shops.AnyAsync(s => s.Id == id && s.Status == CoffeeShopStatus.Active && !s.IsHidden && s.PublicSlug != null, ct),
            "roaster" => await db.Roasters.AnyAsync(r => r.Id == id && r.PublicSlug != null, ct),
            _ => id != Guid.Empty
        };
        if (!exists) throw new DiscoveryException("Public entity not found.", 404, "NOT_FOUND");
        // Wolverine already owns the request transaction. Raw store callers own one here so
        // the default-list advisory lock and favorite/collection writes share the same commit.
        await using var ownedTransaction = db.Database.IsNpgsql() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        if (kind != "drink")
        {
            if (favorite)
            {
                var collection = await SavedCollectionDefault.EnsureAsync(db, userId, ct);
                if (collection.Visibility == "private" && !collection.Items.Any(i => i.Kind == kind && i.EntityId == id))
                    collection.ReplaceItems(collection.Items.OrderBy(i => i.Position)
                        .Select(i => new DiscoveryFavorite(userId, i.Kind, i.EntityId))
                        .Append(new DiscoveryFavorite(userId, kind, id)));
            }
            else
            {
                await SavedCollectionDefault.LockAsync(db, userId, ct);
                var collections = await db.SavedCollections.Include(c => c.Items)
                    .Where(c => c.OwnerId == userId && c.RevokedAtUtc == null &&
                        c.Items.Any(i => i.Kind == kind && i.EntityId == id)).ToArrayAsync(ct);
                foreach (var collection in collections)
                    collection.ReplaceItems(collection.Items.OrderBy(i => i.Position)
                        .Where(i => i.Kind != kind || i.EntityId != id)
                        .Select(i => new DiscoveryFavorite(userId, i.Kind, i.EntityId)));
            }
        }
        var row = await db.DiscoveryFavorites.FindAsync([userId, kind, id], ct);
        if (favorite && row is null) db.DiscoveryFavorites.Add(new DiscoveryFavorite(userId, kind, id));
        if (!favorite && row is not null) db.DiscoveryFavorites.Remove(row);
        // Flush inside Wolverine's transaction so idempotent races can use EF's savepoint before final commit.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) when (!favorite && ex.Entries.Count == 1 && ex.Entries[0].Entity == row)
        {
            // A concurrent DELETE already removed this user's favorite.
            ex.Entries[0].State = EntityState.Detached;
        }
        catch (DbUpdateException ex) when (favorite && ex.Entries.Count == 1 && ex.Entries[0].Entity is DiscoveryFavorite &&
                                          ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_DiscoveryFavorites" })
        {
            // A concurrent idempotent PUT has already inserted the same user's favorite.
            foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
        }
        if (ownedTransaction != null) await ownedTransaction.CommitAsync(ct);
    }

}
