using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Events;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.MediaService.Services.Variants;

namespace CoffeePeek.MediaService.Consumers;

public class DeletePhotoHandler(
    IPhotoRepository photoRepository,
    IUnitOfWork unitOfWork,
    IStorageService storageService,
    IPhotoVariantQueue variants)
{
    public async Task Handle(DeletePhotoFromStorageEvent message, CancellationToken ct)
    {
        var photo = await photoRepository.GetByIdAsync(message.PhotoId, ct);
        
        if (photo == null) return;
        await variants.CancelPhotoAsync(photo.Id, ct);
        if (photo.Status == PhotoStatus.Deleted) return;

        await storageService.Delete(photo.StorageKey, photo.BucketType, ct);

        photo.Status = PhotoStatus.Deleted;
        photo.DeletedAt = DateTime.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
