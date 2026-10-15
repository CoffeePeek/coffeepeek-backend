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
        var shopIds = favorites.Where(f => f.Kind == "coffee_shop").Select(f => f.EntityId).ToArray();
        var roasterIds = favorites.Where(f => f.Kind == "roaster").Select(f => f.EntityId).ToArray();
        var drinkIds = favorites.Where(f => f.Kind == "drink").Select(f => f.EntityId).ToArray();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id) && s.Status == CoffeeShopStatus.Active && !s.IsHidden && s.PublicSlug != null)
            .Select(s => new { s.Id, s.Name, Slug = s.PublicSlug!, Revision = s.SlugRevision,
                PhotoKey = s.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.Id).Select(p => p.StorageKey).FirstOrDefault() })
            .ToDictionaryAsync(s => s.Id, ct);
        var roasters = await db.Roasters.AsNoTracking().Where(r => roasterIds.Contains(r.Id) && r.PublicSlug != null)
            .Select(r => new { r.Id, r.Name, Slug = r.PublicSlug!, Revision = r.SlugRevision,
                PhotoKey = r.Photos.OrderBy(p => p.SortIndex).ThenBy(p => p.Id).Select(p => p.StorageKey).FirstOrDefault() })
            .ToDictionaryAsync(r => r.Id, ct);
        var drinks = await db.CoffeeDrinkDefinitions.AsNoTracking().Where(d => drinkIds.Contains(d.Id) && d.IsActive)
            .Select(d => new { d.Id, d.Slug, d.NameRu }).ToDictionaryAsync(d => d.Id, ct);
        return favorites.OrderByDescending(f => f.CreatedAtUtc).ThenBy(f => f.Kind).ThenBy(f => f.EntityId)
            .Select(f => f.Kind == "coffee_shop" && shops.TryGetValue(f.EntityId, out var shop)
                ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = shop.Name, CoverPhoto = Photo(shop.PhotoKey),
                    Address = new() { Slug = shop.Slug, Revision = shop.Revision, CanonicalPath = $"/coffee-shops/{shop.Slug}" } }
                : f.Kind == "roaster" && roasters.TryGetValue(f.EntityId, out var roaster)
                    ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = roaster.Name, CoverPhoto = Photo(roaster.PhotoKey),
                        Address = new() { Slug = roaster.Slug, Revision = roaster.Revision, CanonicalPath = $"/roasters/{roaster.Slug}" } }
                    : f.Kind == "drink" && drinks.TryGetValue(f.EntityId, out var drink)
                        ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = drink.NameRu,
                            Drink = new() { Slug = drink.Slug } }
                        : null).Where(f => f is not null).Select(f => f!).ToArray();
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
    }

    private Photo? Photo(string? key)
    {
        if (string.IsNullOrEmpty(key) || media is null) return null;
        var options = media.Value;
        var url = MediaStorageUrlBuilder.BuildPublicUrl(options.PublicEndpoint, options.ShopBucketName, key);
        if (url is null) return null;
        return new() { FullUrl = url, Urls = CoffeePeek.Contract.Dtos.PhotoUrlsDto.Create(
            options.PublicEndpoint, options.ImageProxyEndpoint, options.ShopBucketName, key) };
    }
}
