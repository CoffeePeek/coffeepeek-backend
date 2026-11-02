using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Persistance.Discovery;

internal static class DiscoveryFavoriteCards
{
    internal static async Task<DiscoveryFavoriteItem[]> ReadAsync(ShopsDbContext db,
        DiscoveryFavorite[] favorites, IOptions<MediaPublicUrlOptions>? media, CancellationToken ct)
    {
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
        return favorites
            .Select(f => f.Kind == "coffee_shop" && shops.TryGetValue(f.EntityId, out var shop)
                ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = shop.Name, CoverPhoto = Photo(shop.PhotoKey, media),
                    Address = new() { Slug = shop.Slug, Revision = shop.Revision, CanonicalPath = $"/coffee-shops/{shop.Slug}" } }
                : f.Kind == "roaster" && roasters.TryGetValue(f.EntityId, out var roaster)
                    ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = roaster.Name, CoverPhoto = Photo(roaster.PhotoKey, media),
                        Address = new() { Slug = roaster.Slug, Revision = roaster.Revision, CanonicalPath = $"/roasters/{roaster.Slug}" } }
                    : f.Kind == "drink" && drinks.TryGetValue(f.EntityId, out var drink)
                        ? new DiscoveryFavoriteItem { Kind = f.Kind, CreatedAtUtc = f.CreatedAtUtc, Name = drink.NameRu,
                            Drink = new() { Slug = drink.Slug } }
                        : null).Where(f => f is not null).Select(f => f!).ToArray();
    }

    private static Photo? Photo(string? key, IOptions<MediaPublicUrlOptions>? media)
    {
        if (string.IsNullOrEmpty(key) || media is null) return null;
        var options = media.Value;
        var url = MediaStorageUrlBuilder.BuildPublicUrl(options.PublicEndpoint, options.ShopBucketName, key);
        if (url is null) return null;
        return new() { FullUrl = url, Urls = CoffeePeek.Contract.Dtos.PhotoUrlsDto.Create(
            options.PublicEndpoint, options.ImageProxyEndpoint, options.ShopBucketName, key) };
    }
}
