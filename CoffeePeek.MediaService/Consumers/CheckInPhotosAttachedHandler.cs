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
    public Task Handle(CheckInPhotosAttachedEvent message, CancellationToken ct) =>
        ConfirmPhotos(message.UserId, message.StorageKeys, ct);

    public Task Handle(ReviewPhotosAttachedEvent message, CancellationToken ct) =>
        ConfirmPhotos(message.UserId, message.StorageKeys, ct);

    private async Task ConfirmPhotos(Guid userId, IReadOnlyCollection<string> storageKeys, CancellationToken ct)
    {
        if (storageKeys is null || storageKeys.Count == 0) return;

        var photos = await photoRepository.GetByStorageKeysAsync(userId, storageKeys, ct);

        var changed = false;
        foreach (var photo in photos)
        {
            if (photo.OwnerId != userId || photo.Status != PhotoStatus.Pending) continue;

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
