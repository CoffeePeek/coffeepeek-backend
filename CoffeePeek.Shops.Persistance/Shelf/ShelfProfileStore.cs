using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Shelf;

public sealed class ShelfProfileStore(ShopsDbContext db, IShelfCatalogLookup catalog) : IShelfProfileStore
{
    public async Task<ShelfProfileVisibilityDto> VisibilityAsync(Guid owner, CancellationToken ct) =>
        new(await VisibleAsync(owner, ct));

    public async Task<ShelfProfileVisibilityDto> SetVisibilityAsync(Guid owner, bool isVisible, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        var preference = await db.ShelfProfilePreferences.FindAsync([owner], ct);
        if (preference == null) db.ShelfProfilePreferences.Add(preference = new(owner));
        preference.SetVisibility(isVisible);
        await db.SaveChangesAsync(ct);
        return new(isVisible);
    }

    public async Task<PublicShelfDto?> PublicAsync(Guid owner, string profileSlug, CancellationToken ct)
    {
        if (!await VisibleAsync(owner, ct)) return null;
        var bags = await db.ShelfBags.AsNoTracking().Where(b => b.UserId == owner && b.Status == ShelfBagStatus.OnShelf)
            .OrderBy(b => b.Position).ThenBy(b => b.AddedAtUtc).ThenBy(b => b.Id).ToArrayAsync(ct);
        var productIds = bags.Where(b => b.CoffeeProductId != null).Select(b => b.CoffeeProductId!.Value).Distinct().ToArray();
        var slugs = productIds.Length == 0 ? new Dictionary<Guid, string>() : await catalog.GetPublishedSlugsAsync(productIds, ct);
        // Recheck after the asynchronous catalog read: opting out must not complete a stale public projection.
        if (!await VisibleAsync(owner, ct)) return null;
        var items = bags.Select(b => new PublicShelfItemDto(b.Id, b.Name ?? "", b.RoasterName,
            b.CoffeeProductId is { } product && slugs.TryGetValue(product, out var slug) ? slug : null,
            b.PhotoId is { } photo && AllowedType(b.PhotoContentType)
                ? $"/api/v1/profiles/{Uri.EscapeDataString(profileSlug)}/shelf/bags/{b.Id}/photos/{photo}" : null)).ToArray();
        return new(items, items.Length);
    }

    public async Task<ShelfBagPhotoAccess?> PublicPhotoAsync(Guid owner, Guid bagId, Guid photoId, CancellationToken ct)
    {
        if (!await VisibleAsync(owner, ct)) return null;
        var photo = await db.ShelfBags.AsNoTracking().Where(b => b.UserId == owner && b.Id == bagId
                && b.Status == ShelfBagStatus.OnShelf && b.PhotoId == photoId)
            .Select(b => new ShelfBagPhotoAccess(b.UserId, b.Id, photoId, b.PhotoContentType!)).SingleOrDefaultAsync(ct);
        return photo != null && AllowedType(photo.ContentType) ? photo : null;
    }

    public async Task DeleteForOwnerAsync(Guid owner, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        var preference = await db.ShelfProfilePreferences.FindAsync([owner], ct);
        if (preference == null) return;
        db.ShelfProfilePreferences.Remove(preference);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> VisibleAsync(Guid owner, CancellationToken ct) =>
        await db.ShelfProfilePreferences.AsNoTracking().Where(p => p.OwnerId == owner)
            .Select(p => (bool?)p.IsVisible).SingleOrDefaultAsync(ct) ?? true;

    private Task LockOwnerAsync(Guid owner, CancellationToken ct) => db.Database.IsNpgsql()
        ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"shelf:" + owner}, 0))", ct)
        : Task.CompletedTask;

    private static bool AllowedType(string? type) => type?.ToLowerInvariant() is "image/jpeg" or "image/png" or "image/webp";
}
