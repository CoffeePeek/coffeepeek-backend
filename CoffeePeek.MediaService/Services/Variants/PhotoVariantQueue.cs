using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantQueue(MediaDbContext db) : IPhotoVariantQueue
{
    public async Task EnqueueAsync(PhotoMetadata photo, CancellationToken ct)
    {
        if (photo.Status != PhotoStatus.Confirmed || photo.BucketType != BucketType.CheckIn
            || photo.CheckInId is not { } checkInId || photo.ShopAttachmentId != null)
            return;
        var version = CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey);
        // Called inside the attachment consumer's EF/outbox transaction. Unique key handles redelivery.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PhotoVariantJobs"
                ("Id", "PhotoId", "CheckInId", "OwnerId", "SourceKey", "SourceVersion", "RecipeVersion",
                 "Status", "Attempts", "NextAttemptAtUtc", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT {Guid.NewGuid()}, {photo.Id}, {checkInId}, {photo.OwnerId}, {photo.StorageKey},
                   {version}, {PhotoVariantRecipe.Version}, 'Pending', 0, now(), now(), now()
            WHERE NOT EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" WHERE "CheckInId" = {checkInId})
            ON CONFLICT ("PhotoId", "SourceVersion", "RecipeVersion") DO UPDATE
                SET "Priority" = 0, "BackfillRunId" = NULL
                WHERE "PhotoVariantJobs"."Priority" <> 0
            """, ct);
    }

    public async Task CancelCheckInAsync(Guid checkInId, DateTime deletedAtUtc, CancellationToken ct)
    {
        // The tombstone and cancellation are committed together by Wolverine, including delete-before-attach.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CheckInPhotoTombstones" ("CheckInId", "DeletedAtUtc")
            VALUES ({checkInId}, {deletedAtUtc}) ON CONFLICT ("CheckInId") DO NOTHING
            """, ct);
        await db.PhotoVariantJobs.Where(x => x.CheckInId == checkInId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PhotoVariantJobStatus.Cancelled)
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
    }

    public Task CancelPhotoAsync(Guid photoId, CancellationToken ct) => db.PhotoVariantJobs.Where(x => x.PhotoId == photoId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PhotoVariantJobStatus.Cancelled)
            .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
            .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
}
