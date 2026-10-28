using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

/// <summary>A photo removed from a live check-in stops being readable and its bytes are removed. Idempotent.</summary>
public class CheckInPhotoDetachedHandler(IPhotoRepository photoRepository, IStorageService storageService,
    IPhotoVariantQueue variants, IUnitOfWork unitOfWork)
{
    public async Task Handle(CheckInPhotoDetachedEvent message, CancellationToken ct)
    {
        var photo = await photoRepository.GetByIdAsync(message.PhotoId, ct);
        if (photo == null || photo.OwnerId != message.UserId || photo.CheckInId != message.CheckInId
            || photo.BucketType != BucketType.CheckIn || photo.Status == PhotoStatus.Deleted) return;
        await variants.CancelPhotoAsync(photo.Id, ct);
        await storageService.Delete(photo.StorageKey, BucketType.CheckIn, ct);
        photo.Status = PhotoStatus.Deleted;
        photo.DeletedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
