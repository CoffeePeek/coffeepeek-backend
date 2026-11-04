using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class SavedCollectionsStore
{
    private async Task<List<DiscoveryFavorite>> SelectionsAsync(Guid owner, FavoriteCollectionSelection[] items,
        IReadOnlyCollection<SavedCollectionItem> existing, CancellationToken ct)
    {
        if (items == null) throw new DiscoveryException("Items are required.", 400, "INVALID_COLLECTION");
        var selected = new List<DiscoveryFavorite>();
        var seen = new HashSet<(string Kind, Guid Id)>();
        foreach (var item in items)
        {
            if (item == null) throw new DiscoveryException("Invalid place.", 400, "INVALID_COLLECTION");
            var id = await ResolveAsync(item.Kind, item.Slug, ct);
            if (id == Guid.Empty || !seen.Add((item.Kind, id)))
                throw new DiscoveryException("Select distinct available places.", 400, "INVALID_COLLECTION");
            selected.Add(new(owner, item.Kind, id));
        }
        await EnsureVisibleAsync(selected.ToArray(), ct);
        return selected;
    }

    private async Task EnsureVisibleAsync(DiscoveryFavorite[] items, CancellationToken ct)
    {
        var visible = await DiscoveryFavoriteCards.ReadAsync(db, items, media, ct);
        if (visible.Length != items.Length) throw new DiscoveryException("Place is no longer available.", 409, "COLLECTION_CHANGED");
    }

    private async Task<Guid> ResolveAsync(string? kind, string? slug, CancellationToken ct)
    {
        if (kind is not ("coffee_shop" or "roaster") || !PublicSlug.IsValid(slug))
            throw new DiscoveryException("Invalid collection place.", 400, "INVALID_COLLECTION");
        if (db.PublicAddressesEnabled)
            return (await addresses.ResolveAsync(kind == "coffee_shop" ? PublicAddressKind.CoffeeShop : PublicAddressKind.Roaster, slug!, ct))?.EntityId ?? Guid.Empty;
        return kind == "coffee_shop"
            ? await db.Shops.Where(s => s.PublicSlug == slug).Select(s => s.Id).SingleOrDefaultAsync(ct)
            : await db.Roasters.Where(r => r.PublicSlug == slug).Select(r => r.Id).SingleOrDefaultAsync(ct);
    }

    private static void ValidateDetails(string title, string? description, string visibility)
    {
        try { SavedCollection.ValidateDetails(title, description, visibility); }
        catch (DomainException) { throw new DiscoveryException("Invalid collection details.", 400, "INVALID_COLLECTION"); }
    }

    private static string? UploadKey(UploadedPhotoDto? upload)
    {
        if (upload == null) return null;
        var key = upload.StorageKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 255 || !key.StartsWith("collection-covers/", StringComparison.Ordinal))
            throw new DiscoveryException("Use a collection-cover upload.", 400, "INVALID_COLLECTION");
        return key;
    }

    private async Task<SavedCollectionCover> ClaimAsync(Guid owner, Guid id, string key, Guid? currentPhoto, CancellationToken ct)
    {
        var photo = await covers.ClaimAsync(owner, id, key, ct);
        var used = await db.SavedCollectionCoverUses.FindAsync([photo.PhotoId], ct);
        if (used != null && (used.CollectionId != id || currentPhoto != photo.PhotoId)) throw Conflict();
        if (used == null) db.SavedCollectionCoverUses.Add(new(photo.PhotoId, id));
        return new(photo.PhotoId, photo.StorageKey, photo.ContentType);
    }
}
