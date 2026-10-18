using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Photo;
using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services.Variants;

public static class PhotoVariantManifest
{
    public static CheckInPhotoVariantsReadyEvent Create(PhotoVariantJob job, IEnumerable<PhotoVariant> variants) =>
        new(job.Id, job.CheckInId, job.OwnerId, job.PhotoId, job.SourceVersion, job.RecipeVersion,
            variants.OrderBy(v => v.Purpose).ThenBy(v => v.Format).Select(v =>
                new CheckInPhotoVariantInfo(v.Purpose, v.Format, v.ContentType, v.Width, v.Height,
                    v.SizeBytes, v.Digest)).ToArray());
}
