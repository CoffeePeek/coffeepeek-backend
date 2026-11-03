using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Persistance.Discovery;

/// <summary>Discovery-owned writes, enlisted in the Wolverine transaction; no cross-module writes.</summary>
public sealed class FavoriteCollectionsStore(ShopsDbContext db, IPublicAddressService addresses,
    IOptions<MediaPublicUrlOptions>? media = null) : IFavoriteCollectionsStore
{
    public async Task<FavoriteCollectionDto> CreateAsync(Guid owner, CreateFavoriteCollectionRequest request, CancellationToken ct)
    {
        Validate(request);
        if (await db.FavoriteCollections.CountAsync(c => c.OwnerId == owner && c.RevokedAtUtc == null, ct) >= 100)
            throw new DiscoveryException("Revoke an existing collection before creating another.", 409, "COLLECTION_LIMIT");
        var favorites = await db.DiscoveryFavorites.AsNoTracking().Where(f => f.UserId == owner).ToArrayAsync(ct);
        var selected = new List<DiscoveryFavorite>();
        foreach (var item in request.Items)
        {
            var entityId = await ResolveAsync(item, ct);
            var favorite = favorites.SingleOrDefault(f => f.Kind == item.Kind && f.EntityId == entityId);
            if (favorite is null || selected.Any(f => f.Kind == item.Kind && f.EntityId == entityId))
                throw new DiscoveryException("Select distinct places from your favorites.", 400, "INVALID_COLLECTION");
            selected.Add(favorite);
        }
        var visible = await DiscoveryFavoriteCards.ReadAsync(db, selected.ToArray(), media, ct);
        if (visible.Length != selected.Count)
            throw new DiscoveryException("A selected place is no longer available.", 409, "COLLECTION_CHANGED");
        var collection = new FavoriteCollection(owner, request.Title, selected);
        var response = await MapAsync(collection, visible, ct);
        db.FavoriteCollections.Add(collection);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<FavoriteCollectionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var collection = await db.FavoriteCollections.AsNoTracking().Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == id && c.RevokedAtUtc == null, ct);
        if (collection is null) return null;
        var references = collection.Items.OrderBy(i => i.Position)
            .Select(i => new DiscoveryFavorite(collection.OwnerId, i.Kind, i.EntityId)).ToArray();
        return await MapAsync(collection, await DiscoveryFavoriteCards.ReadAsync(db, references, media, ct), ct);
    }

    public async Task<FavoriteCollectionSummary[]> ListAsync(Guid owner, CancellationToken ct)
    {
        var rows = await db.FavoriteCollections.AsNoTracking().Where(c => c.OwnerId == owner && c.RevokedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id).Take(100)
            .Select(c => new { c.Id, c.Title, c.CreatedAtUtc, ItemCount = c.Items.Count })
            .ToArrayAsync(ct);
        return rows.Select(c => new FavoriteCollectionSummary(c.Id, c.Title, $"/collections/{c.Id:N}", c.CreatedAtUtc, c.ItemCount)).ToArray();
    }

    public async Task<bool> RevokeAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var collection = await db.FavoriteCollections.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == owner, ct);
        if (collection is null) return false;
        collection.Revoke();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task DeleteForOwnerAsync(Guid owner, CancellationToken ct)
    {
        var collections = await db.FavoriteCollections.Include(c => c.Items).Where(c => c.OwnerId == owner).ToArrayAsync(ct);
        db.FavoriteCollections.RemoveRange(collections);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid> ResolveAsync(FavoriteCollectionSelection item, CancellationToken ct)
    {
        if (db.PublicAddressesEnabled)
            return (await addresses.ResolveAsync(item.Kind == "coffee_shop" ? PublicAddressKind.CoffeeShop : PublicAddressKind.Roaster,
                item.Slug, ct))?.EntityId ?? Guid.Empty;
        return item.Kind == "coffee_shop"
            ? await db.Shops.Where(s => s.PublicSlug == item.Slug).Select(s => s.Id).SingleOrDefaultAsync(ct)
            : await db.Roasters.Where(s => s.PublicSlug == item.Slug).Select(s => s.Id).SingleOrDefaultAsync(ct);
    }

    private static void Validate(CreateFavoriteCollectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 100 || request.Title.Any(char.IsControl) ||
            request.Items is null || request.Items.Length is < 1 or > 100 ||
            request.Items.Any(i => i is null || i.Kind is not ("coffee_shop" or "roaster") || !PublicSlug.IsValid(i.Slug)))
            throw new DiscoveryException("Invalid collection title or selection.", 400, "INVALID_COLLECTION");
    }

    private async Task<FavoriteCollectionDto> MapAsync(FavoriteCollection collection, DiscoveryFavoriteItem[] items, CancellationToken ct)
    {
        var shopSlugs = items.Where(i => i.Kind == "coffee_shop").Select(i => i.Address!.Slug).ToArray();
        var shops = await db.Shops.AsNoTracking().Where(s => shopSlugs.Contains(s.PublicSlug!))
            .Select(s => new { s.Id, s.PublicSlug, StreetAddress = s.Location == null ? null : s.Location.Address,
                CityId = s.Location == null ? (Guid?)null : s.Location.CityId }).ToDictionaryAsync(s => s.PublicSlug!, ct);
        var roasterSlugs = items.Where(i => i.Kind == "roaster").Select(i => i.Address!.Slug).ToArray();
        var roasters = await db.Roasters.AsNoTracking().Where(r => roasterSlugs.Contains(r.PublicSlug!))
            .Select(r => new { r.Id, r.PublicSlug, StreetAddress = r.Location == null ? null : r.Location.Address,
                CityId = r.Location == null ? (Guid?)null : r.Location.CityId }).ToDictionaryAsync(r => r.PublicSlug!, ct);
        var shopIds = shops.Values.Select(s => s.Id).ToArray();
        var counts = await CheckInEligibility.Public(db).Where(c => c.ShopId != null && shopIds.Contains(c.ShopId.Value))
            .GroupBy(c => c.ShopId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var ratings = await CheckInEligibility.LatestRatings(db).Where(c => c.ShopId != null && shopIds.Contains(c.ShopId.Value))
            .GroupBy(c => c.ShopId!.Value).Select(g => new { Id = g.Key, Rating = g.Average(c => (c.Rating!.Place + c.Rating.Coffee + c.Rating.Service) / 3m) })
            .ToDictionaryAsync(g => g.Id, g => (decimal?)Math.Round(g.Rating, 2), ct);
        var cityIds = shops.Values.Select(s => s.CityId).Concat(roasters.Values.Select(r => r.CityId)).ToArray();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return new() { Id = collection.Id, Title = collection.Title, CanonicalPath = $"/collections/{collection.Id:N}",
            CreatedAtUtc = collection.CreatedAtUtc, Items = items.OrderBy(item => item.Kind == "coffee_shop" ? 0 : 1).Select(item => {
                var shop = item.Kind == "coffee_shop" ? shops.GetValueOrDefault(item.Address!.Slug) : null;
                var location = shop ?? roasters.GetValueOrDefault(item.Address!.Slug);
                return new FavoriteCollectionPlace { Kind = item.Kind, Address = item.Address, Name = item.Name,
                    CoverPhoto = item.CoverPhoto, CreatedAtUtc = collection.CreatedAtUtc, StreetAddress = location?.StreetAddress,
                    CityName = location?.CityId is { } cityId ? cities.GetValueOrDefault(cityId) : null,
                    Rating = shop is null ? null : ratings.GetValueOrDefault(shop.Id), CheckInCount = shop is null ? null : counts.GetValueOrDefault(shop.Id) };
            }).ToArray() };
    }
}
