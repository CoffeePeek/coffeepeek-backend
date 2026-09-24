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
            if (photo.OwnerId != message.UserId || photo.Status != PhotoStatus.Pending) continue;

            // Tag storage first: if MinIO throws, the photo stays Pending and the retry redoes it.
            // MarkAsPermanent is idempotent.
            await storageService.MarkAsPermanent(photo.StorageKey, BucketType.Shop, ct);
            photo.Status = PhotoStatus.Confirmed;
            changed = true;
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
