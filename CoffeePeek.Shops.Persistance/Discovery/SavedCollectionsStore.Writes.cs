using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class SavedCollectionsStore
{
    public async Task<SavedCollectionDto> CreateAsync(Guid owner, CreateSavedCollectionRequest request, CancellationToken ct)
    {
        ValidateDetails(request.Title, request.Description, request.Visibility);
        var storageKey = UploadKey(request.CoverUpload);
        var id = storageKey == null ? Guid.NewGuid() : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            "collection-cover:" + owner.ToString("N") + ":" + storageKey)).AsSpan(0, 16));
        await LockOwnerAsync(owner, ct);
        if (await db.SavedCollections.AnyAsync(c => c.Id == id, ct) || await db.FavoriteCollections.AnyAsync(c => c.Id == id, ct))
            throw Conflict();
        if (await Lists(owner).CountAsync(ct) >= 100)
            throw new DiscoveryException("Delete a collection before creating another.", 409, "COLLECTION_LIMIT");
        var selected = await SelectionsAsync(owner, request.Items, [], ct);
        var photo = storageKey == null ? null : await ClaimAsync(owner, id, storageKey, null, ct);
        var row = SavedCollection.CreateList(owner, id, request.Title, request.Description, request.Visibility, photo, selected);
        db.SavedCollections.Add(row);
        await FlushAsync(ct);
        if (photo != null) await bus.PublishAsync(new CollectionCoverAttachedEvent(owner, id, photo.PhotoId));
        return await DetailAsync(row, ct);
    }

    public async Task<SavedCollectionDto?> UpdateAsync(Guid owner, Guid id, UpdateSavedCollectionRequest request, CancellationToken ct)
    {
        ValidateDetails(request.Title, request.Description, request.Visibility);
        if (request.RemoveCover && request.CoverUpload != null)
            throw new DiscoveryException("Choose either a replacement cover or removal.", 400, "INVALID_COLLECTION");
        var storageKey = UploadKey(request.CoverUpload);
        await LockOwnerAsync(owner, ct);
        var row = await OwnedAsync(owner, id, ct);
        if (row == null) return null;
        EnsureRevision(row, request.ExpectedRevision);
        var oldPhoto = row.CoverPhotoId;
        var photo = storageKey == null ? null : await ClaimAsync(owner, id, storageKey, oldPhoto, ct);
        row.Edit(request.Title, request.Description, request.Visibility, photo, request.RemoveCover);
        if (row.Visibility != "public") await ClearFeaturedAsync(owner, id, ct);
        await FlushAsync(ct);
        if (photo != null && photo.PhotoId != oldPhoto) await bus.PublishAsync(new CollectionCoverAttachedEvent(owner, id, photo.PhotoId));
        if (oldPhoto is { } released && released != row.CoverPhotoId)
            await bus.PublishAsync(new CollectionCoverReleasedEvent(owner, id, released, DateTime.UtcNow));
        return await DetailAsync(row, ct);
    }

    public async Task<SavedCollectionDto?> ReplaceItemsAsync(Guid owner, Guid id, ReplaceSavedCollectionItemsRequest request, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        var row = await OwnedAsync(owner, id, ct);
        if (row == null) return null;
        EnsureRevision(row, request.ExpectedRevision);
        var selected = await SelectionsAsync(owner, request.Items, row.Items, ct);
        row.ReplaceItems(selected);
        await FlushAsync(ct);
        return await DetailAsync(row, ct);
    }

    public async Task<bool> DeleteAsync(Guid owner, Guid id, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        var row = await db.SavedCollections.Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.OwnerId == owner && c.Id == id, ct);
        if (row == null) return false;
        if (row.RevokedAtUtc != null) return true;
        row.Revoke();
        await ClearFeaturedAsync(owner, id, ct);
        await FlushAsync(ct);
        if (row.CoverPhotoId is { } photo)
            await bus.PublishAsync(new CollectionCoverReleasedEvent(owner, id, photo, DateTime.UtcNow));
        return true;
    }

    public async Task DeleteForOwnerAsync(Guid owner, CancellationToken ct)
    {
        await LockOwnerAsync(owner, ct);
        var rows = await db.SavedCollections.Include(c => c.Items).Where(c => c.OwnerId == owner).ToArrayAsync(ct);
        var preference = await db.SavedCollectionPreferences.FindAsync([owner], ct);
        if (preference != null) db.SavedCollectionPreferences.Remove(preference);
        db.SavedCollections.RemoveRange(rows);
        await FlushAsync(ct);
        foreach (var row in rows.Where(r => r.CoverPhotoId.HasValue))
            await bus.PublishAsync(new CollectionCoverReleasedEvent(owner, row.Id, row.CoverPhotoId!.Value, DateTime.UtcNow));
    }

    private Task<SavedCollection?> OwnedAsync(Guid owner, Guid id, CancellationToken ct) =>
        Lists(owner).Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == id, ct);

    private async Task ClearFeaturedAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var preference = await db.SavedCollectionPreferences.FindAsync([owner], ct);
        if (preference?.CollectionId == id) preference.Select(null);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
    }

    private async Task LockOwnerAsync(Guid owner, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Collection writes require the owning transaction.");
        var key = "saved-collections:" + owner.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    private static void EnsureRevision(SavedCollection row, long expected)
    {
        if (expected < 1 || row.Revision != expected) throw Conflict();
    }

    private static DiscoveryException Conflict() => new("Collection changed; reload and retry.", 409, "COLLECTION_CONFLICT");
}
