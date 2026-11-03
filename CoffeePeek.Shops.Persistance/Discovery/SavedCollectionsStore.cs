using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class SavedCollectionsStore(ShopsDbContext db, IPublicAddressService addresses,
    ICollectionCoverStore covers, IMessageBus bus, IOptions<MediaPublicUrlOptions>? media = null) : ISavedCollectionsStore
{
    public async Task<SavedCollectionSummary[]> ListAsync(Guid owner, string? kind, string? slug, CancellationToken ct)
    {
        Guid? target = null;
        if (kind != null || slug != null) target = await ResolveAsync(kind, slug, ct);
        var rows = await Lists(owner).AsNoTracking().Include(c => c.Items)
            .OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToArrayAsync(ct);
        var featured = await FeaturedAsync(owner, ct);
        return rows.Select(c => Summary(c, featured.CollectionId, kind, target)).ToArray();
    }

    public async Task<SavedCollectionDto?> GetAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var row = await Lists(owner).AsNoTracking().Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == id, ct);
        return row == null ? null : await DetailAsync(row, ct);
    }

    public async Task<FavoriteCollectionDto?> GetPublicAsync(Guid id, CancellationToken ct)
    {
        var row = await db.SavedCollections.AsNoTracking().Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == id && c.RevokedAtUtc == null && c.Visibility == "public", ct);
        return row == null ? null : await new CollectionCardsProjection(db, media).ReadAsync(row, ct);
    }

    public async Task<FeaturedCollectionPreference> FeaturedAsync(Guid owner, CancellationToken ct) =>
        new(await db.SavedCollectionPreferences.AsNoTracking().Where(p => p.OwnerId == owner)
            .Select(p => p.CollectionId).SingleOrDefaultAsync(ct));

    public async Task<SavedCollectionDto?> PublicFeaturedAsync(Guid owner, CancellationToken ct)
    {
        var preference = await FeaturedAsync(owner, ct);
        if (preference.CollectionId == null) return null;
        var row = await Lists(owner).AsNoTracking().Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == preference.CollectionId && c.Visibility == "public", ct);
        return row == null ? null : await DetailAsync(row, ct);
    }

    public async Task<CollectionCoverAccess?> CoverAsync(Guid? owner, Guid id, Guid photoId, CancellationToken ct)
    {
        var row = await db.SavedCollections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id
            && c.RevokedAtUtc == null && c.CoverPhotoId == photoId
            && (owner == null ? c.Visibility == "public" : c.OwnerId == owner), ct);
        return row?.CoverContentType is { } type ? new(row.OwnerId, id, photoId, type) : null;
    }

    private IQueryable<SavedCollection> Lists(Guid owner) =>
        db.SavedCollections.Where(c => c.OwnerId == owner && c.RevokedAtUtc == null);

    private async Task<SavedCollectionDto> DetailAsync(SavedCollection row, CancellationToken ct)
    {
        var projection = await new CollectionCardsProjection(db, media).ReadAsync(row, ct);
        var featured = await FeaturedAsync(row.OwnerId, ct);
        return new(row.Id, row.Title, row.Description, row.Visibility, projection.CoverPhoto, row.Items.Count,
            row.Revision, featured.CollectionId == row.Id, row.Visibility == "public" ? projection.CanonicalPath : null,
            row.CreatedAtUtc, row.UpdatedAtUtc, projection.Items);
    }

    private static SavedCollectionSummary Summary(SavedCollection row, Guid? featured, string? kind, Guid? target) =>
        new(row.Id, row.Title, row.Description, row.Visibility, Cover(row), row.Items.Count, row.Revision,
            target.HasValue && row.Items.Any(i => i.Kind == kind && i.EntityId == target), featured == row.Id,
            row.Visibility == "public" ? $"/collections/{row.Id:N}" : null);

    private static SavedCollectionCoverDto? Cover(SavedCollection row) => row.CoverPhotoId is { } photoId
        ? new(photoId, $"/api/v1/{(row.Visibility == "public" ? "favorite-collections" : "saved-collections")}/{row.Id}/cover/{photoId}")
        : null;
}
