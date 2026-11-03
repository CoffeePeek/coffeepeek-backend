using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Persistance.Discovery;

internal sealed class CollectionCardsProjection(ShopsDbContext db, IOptions<MediaPublicUrlOptions>? media)
{
    internal async Task<FavoriteCollectionDto> ReadAsync(SavedCollection collection, CancellationToken ct)
    {
        var references = collection.Items.OrderBy(i => i.Position)
            .Select(i => new DiscoveryFavorite(collection.OwnerId, i.Kind, i.EntityId)).ToArray();
        return await MapAsync(new CollectionProjectionSource(collection.Id, collection.Title, collection.CreatedAtUtc,
            "list", collection.Visibility, collection.Description, collection.CoverPhotoId, collection.Revision, collection.UpdatedAtUtc),
            await DiscoveryFavoriteCards.ReadAsync(db, references, media, ct), ct);
    }

    internal Task<FavoriteCollectionDto> MapAsync(FavoriteCollection collection, DiscoveryFavoriteItem[] items, CancellationToken ct) =>
        MapAsync(new CollectionProjectionSource(collection.Id, collection.Title, collection.CreatedAtUtc,
            "snapshot", "public", null, null, 1, collection.CreatedAtUtc), items, ct);

    private async Task<FavoriteCollectionDto> MapAsync(CollectionProjectionSource collection, DiscoveryFavoriteItem[] items, CancellationToken ct)
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
            Mode = collection.Mode, Visibility = collection.Visibility, Description = collection.Description,
            Revision = collection.Revision, UpdatedAtUtc = collection.UpdatedAtUtc,
            CoverPhoto = collection.CoverPhotoId is { } photoId ? new SavedCollectionCoverDto(photoId,
                collection.Visibility == "public" ? $"/api/v1/favorite-collections/{collection.Id}/cover/{photoId}"
                    : $"/api/v1/saved-collections/{collection.Id}/cover/{photoId}") : null,
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
