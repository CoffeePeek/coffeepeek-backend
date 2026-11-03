using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

/// <summary>A replaced or deleted collection cover stops being readable and its bytes are removed. Idempotent.</summary>
public class CollectionCoverReleasedHandler(IPhotoRepository photoRepository, IStorageService storageService, IUnitOfWork unitOfWork)
{
    public async Task Handle(CollectionCoverReleasedEvent message, CancellationToken ct)
    {
        var photo = await photoRepository.GetByIdAsync(message.PhotoId, ct);
        if (photo == null || photo.OwnerId != message.UserId || photo.CollectionId != message.CollectionId) return;
        var key = photo.OriginalStorageKey ?? photo.StorageKey;
        await using var claimLock = await photoRepository.AcquireClaimLocksAsync(message.UserId, [key], ct);
        photo = (await photoRepository.GetClaimPhotosAsync(message.UserId, [key], ct)).SingleOrDefault(p => p.Id == message.PhotoId);
        if (photo == null || photo.OwnerId != message.UserId || photo.CollectionId != message.CollectionId
            || photo.Status == PhotoStatus.Deleted) return;
        await storageService.Delete(photo.StorageKey, BucketType.CheckIn, ct);
        photo.Status = PhotoStatus.Deleted;
        photo.DeletedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
