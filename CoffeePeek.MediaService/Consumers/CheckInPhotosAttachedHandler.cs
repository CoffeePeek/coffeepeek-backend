using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.MediaService.Services.Variants;

namespace CoffeePeek.MediaService.Consumers;

public class CheckInPhotosAttachedHandler(
    IPhotoRepository photoRepository,
    IUnitOfWork unitOfWork,
    IStorageService storageService,
    IPhotoVariantQueue variants)
{
    public async Task Handle(CheckInPhotosAttachedEvent message, CancellationToken ct)
    {
        if (message.StorageKeys is null || message.StorageKeys.Count == 0) return;

        var photos = await photoRepository.GetByStorageKeysAsync(message.UserId, message.StorageKeys, ct);

        var changed = false;
        foreach (var photo in photos)
        {
            if (photo.OwnerId != message.UserId || photo.Status is not (PhotoStatus.Pending or PhotoStatus.Confirmed)
                || photo.ShopAttachmentId != null) continue;
            if (photo.CheckInId is { } assignedId && assignedId != message.CheckInId) continue;
            // A released private reservation must not be resurrected by a delayed attachment event.
            if (photo.BucketType == BucketType.CheckIn && photo.CheckInId != message.CheckInId) continue;

            if (photo.Status == PhotoStatus.Pending)
            {
                // Tag storage first: a failed operation retains Pending and can be retried.
                await storageService.MarkAsPermanent(photo.StorageKey, photo.BucketType, ct);
                photo.CheckInId = message.CheckInId;
                photo.Status = PhotoStatus.Confirmed;
                changed = true;
            }
            if (photo.BucketType == BucketType.CheckIn)
                await variants.EnqueueAsync(photo, ct);
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
