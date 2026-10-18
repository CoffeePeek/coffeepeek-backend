using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantReads(MediaDbContext db)
{
    public IQueryable<PhotoVariantJob> ReadyJobs() => db.PhotoVariantJobs.AsNoTracking().Where(j =>
        j.Status == PhotoVariantJobStatus.Ready && !db.CheckInPhotoTombstones.Any(t => t.CheckInId == j.CheckInId)
        && db.Photos.Any(p => p.Id == j.PhotoId && p.Status == PhotoStatus.Confirmed && p.BucketType == BucketType.CheckIn
            && p.CheckInId == j.CheckInId && p.OwnerId == j.OwnerId && p.StorageKey == j.SourceKey && p.ShopAttachmentId == null));

    public Task<PhotoVariant?> FindAsync(Guid ownerId, Guid checkInId, Guid photoId, string source, int recipe,
        string purpose, string format, CancellationToken ct) => db.PhotoVariants.AsNoTracking().Where(v =>
            v.PhotoId == photoId && v.SourceVersion == source && v.RecipeVersion == recipe && v.Purpose == purpose && v.Format == format
            && ReadyJobs().Any(j => j.Id == v.JobId && j.OwnerId == ownerId && j.CheckInId == checkInId))
        .SingleOrDefaultAsync(ct);
}
