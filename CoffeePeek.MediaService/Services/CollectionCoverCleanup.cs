using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services;

/// <summary>Bounded cleanup of private cover uploads abandoned for at least 24 hours.</summary>
public sealed class CollectionCoverCleanup(MediaDbContext db, IPhotoRepository repository, IStorageService storage,
    ICollectionCoverReferenceLookup references, TimeProvider clock, IUnitOfWork unitOfWork)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        var candidates = await db.Photos.AsNoTracking().Where(p => (p.Status == PhotoStatus.Pending || p.Status == PhotoStatus.Deleted) && p.UploadedAt < cutoff
                && p.BucketType == BucketType.CheckIn && p.StorageKey.StartsWith("collection-covers/"))
            .OrderBy(p => p.UploadedAt).Take(100).Select(p => new { p.Id, p.OwnerId, Key = p.OriginalStorageKey ?? p.StorageKey }).ToArrayAsync(ct);
        foreach (var candidate in candidates)
        {
            await using var claimLock = await repository.AcquireClaimLocksAsync(candidate.OwnerId, [candidate.Key], ct);
            var photo = (await repository.GetClaimPhotosAsync(candidate.OwnerId, [candidate.Key], ct)).SingleOrDefault(p => p.Id == candidate.Id);
            if (photo == null || photo.Status is not (PhotoStatus.Pending or PhotoStatus.Deleted) || photo.UploadedAt >= cutoff) continue;
            bool? current = photo.Status == PhotoStatus.Pending && photo.CollectionId is { } collectionId && photo.CollectionCoverValidatedAtUtc != null
                ? await references.IsCurrentAsync(photo.OwnerId, collectionId, photo.Id, ct) : false;
            if (current == null) continue;
            if (current == true) await ConfirmAsync(photo, ct);
            else await DeleteAsync(photo, ct);
        }
    }

    private async Task ConfirmAsync(PhotoMetadata photo, CancellationToken ct)
    {
        await storage.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, ct);
        photo.Status = PhotoStatus.Confirmed;
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task DeleteAsync(PhotoMetadata photo, CancellationToken ct)
    {
        await storage.Delete(photo.StorageKey, BucketType.CheckIn, ct);
        if (photo.OriginalStorageKey is { } original && original != photo.StorageKey)
            await storage.Delete(original, BucketType.CheckIn, ct);
        // Sealing may have copied the object before its new key could be saved in metadata.
        var sealedKey = CollectionCoverPhotoStorageKey.Sealed(photo.Id, photo.OriginalStorageKey ?? photo.StorageKey);
        if (sealedKey != photo.StorageKey && sealedKey != photo.OriginalStorageKey)
            await storage.Delete(sealedKey, BucketType.CheckIn, ct);
        db.Photos.Remove(photo);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
