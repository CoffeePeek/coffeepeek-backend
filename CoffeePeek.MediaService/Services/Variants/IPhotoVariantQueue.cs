using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services.Variants;

public interface IPhotoVariantQueue
{
    Task EnqueueAsync(PhotoMetadata photo, CancellationToken ct);
    Task CancelCheckInAsync(Guid checkInId, DateTime deletedAtUtc, CancellationToken ct);
    Task CancelPhotoAsync(Guid photoId, CancellationToken ct);
}
