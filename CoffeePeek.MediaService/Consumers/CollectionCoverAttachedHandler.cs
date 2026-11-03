using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

/// <summary>Only a committed Discovery attachment makes a claimed cover permanent.</summary>
public sealed class CollectionCoverAttachedHandler(IPhotoRepository repository, IStorageService storage, IUnitOfWork unitOfWork)
{
    public async Task Handle(CollectionCoverAttachedEvent message, CancellationToken ct)
    {
        var photo = await repository.GetByIdAsync(message.PhotoId, ct);
        if (photo == null || photo.OwnerId != message.UserId || photo.CollectionId != message.CollectionId) return;
        var key = photo.OriginalStorageKey ?? photo.StorageKey;
        await using var claimLock = await repository.AcquireClaimLocksAsync(message.UserId, [key], ct);
        photo = (await repository.GetClaimPhotosAsync(message.UserId, [key], ct)).SingleOrDefault(p => p.Id == message.PhotoId);
        if (photo == null || photo.OwnerId != message.UserId || photo.CollectionId != message.CollectionId
            || photo.BucketType != BucketType.CheckIn || photo.Status != PhotoStatus.Pending
            || photo.CollectionCoverValidatedAtUtc == null
            || !photo.StorageKey.StartsWith("collection-covers/sealed/", StringComparison.Ordinal)) return;
        await storage.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, ct);
        photo.Status = PhotoStatus.Confirmed;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
