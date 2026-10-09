using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.MediaService.Consumers;

public sealed class ShopPhotosAttachedHandler(IPhotoRepository photos, IStorageService storage,
    IUnitOfWork unitOfWork)
{
    public async Task Handle(ShopPhotosAttachedEvent message, CancellationToken ct)
    {
        if (message.StorageKeys is null || message.StorageKeys.Count == 0) return;
        var resolved = await photos.GetByStorageKeysAsync(message.OwnerId, message.StorageKeys, ct);
        var ordered = message.StorageKeys.Select(key => resolved.Where(p => p.StorageKey == key).Take(2).ToArray())
            .ToArray();
        if (ordered.Any(matches => matches.Length != 1 || matches[0].OwnerId != message.OwnerId
                || matches[0].ShopAttachmentId != message.ShopId
                || matches[0].ShopAttachmentKind != message.Kind
                || matches[0].BucketType != BucketType.Shop || matches[0].CheckInId != null))
            throw new InvalidOperationException("Committed shop photo claim could not be verified.");

        var changed = false;
        foreach (var match in ordered)
        {
            var photo = match[0];
            if (photo.Status == PhotoStatus.Confirmed) continue;
            if (photo.Status != PhotoStatus.Pending)
                throw new InvalidOperationException("Committed shop photo is no longer pending.");
            await storage.MarkAsPermanent(photo.StorageKey, BucketType.Shop, ct);
            photo.Status = PhotoStatus.Confirmed;
            changed = true;
        }
        if (changed) await unitOfWork.SaveChangesAsync(ct);
    }
}
