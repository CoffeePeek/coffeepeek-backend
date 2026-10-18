using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Consumers;

public sealed class CheckInPhotosDeletedHandler(IPhotoVariantQueue variants, MediaDbContext db)
{
    public async Task Handle(CheckInPhotosDeletedEvent message, CancellationToken ct)
    {
        await variants.CancelCheckInAsync(message.CheckInId, message.DeletedAtUtc, ct);
        await db.SaveChangesAsync(ct);
    }
}
