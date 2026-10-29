using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

/// <summary>A replaced or deleted shelf-bag card stops being readable and its bytes are removed. Idempotent.</summary>
public class ShelfBagPhotoReleasedHandler(IPhotoRepository photoRepository, IStorageService storageService, IUnitOfWork unitOfWork)
{
    public async Task Handle(ShelfBagPhotoReleasedEvent message, CancellationToken ct)
    {
        var photo = await photoRepository.GetByIdAsync(message.PhotoId, ct);
        if (photo == null || photo.OwnerId != message.UserId || photo.ShelfBagId != message.BagId
            || photo.Status == PhotoStatus.Deleted) return;
        await storageService.Delete(photo.StorageKey, BucketType.CheckIn, ct);
        photo.Status = PhotoStatus.Deleted;
        photo.DeletedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
