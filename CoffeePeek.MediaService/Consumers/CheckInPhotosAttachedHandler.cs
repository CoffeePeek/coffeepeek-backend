using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

public class CheckInPhotosAttachedHandler(
    IPhotoRepository photoRepository,
    IUnitOfWork unitOfWork,
    IStorageService storageService)
{
    public async Task Handle(CheckInPhotosAttachedEvent message, CancellationToken ct)
    {
        if (message.StorageKeys is null || message.StorageKeys.Count == 0) return;

        var photos = await photoRepository.GetByStorageKeysAsync(message.UserId, message.StorageKeys, ct);

        var changed = false;
        foreach (var photo in photos)
        {
            if (photo.OwnerId != message.UserId || photo.Status != PhotoStatus.Pending
                || photo.ShopAttachmentId != null) continue;
            if (photo.CheckInId is { } assignedId && assignedId != message.CheckInId) continue;
            // A released private reservation must not be resurrected by a delayed attachment event.
            if (photo.BucketType == BucketType.CheckIn && photo.CheckInId != message.CheckInId) continue;

            // Tag storage first: a failed operation retains Pending and can be retried.
            await storageService.MarkAsPermanent(photo.StorageKey, photo.BucketType, ct);
            photo.CheckInId = message.CheckInId;
            photo.Status = PhotoStatus.Confirmed;
            changed = true;
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
