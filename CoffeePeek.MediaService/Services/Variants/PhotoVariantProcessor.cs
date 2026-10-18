using System.Security.Cryptography;
using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantProcessor(PhotoVariantJobStore jobs, ICheckInPhotoReader originals,
    PhotoVariantRenderer renderer, PhotoVariantStorage storage)
{
    public async Task<bool> ProcessAsync(PhotoVariantJob job, CancellationToken ct)
    {
        await storage.RequirePrivateAsync(ct);
        using var source = new PhotoSourceDigestStream();
        await originals.CopyToAsync(job.SourceKey, source, ct);
        var digest = source.Digest();
        var variants = new List<PhotoVariant>();
        foreach (var purpose in PhotoVariantRecipe.Sizes.Keys)
        {
            var webp = await renderer.RenderAsync(job.SourceKey, purpose, "webp", ct);
            if (!await SaveAsync(webp, purpose, "webp")) return false;
            var fallback = webp.HasAlpha ? "png" : "jpeg";
            var image = await renderer.RenderAsync(job.SourceKey, purpose, fallback, ct);
            if (image.Width != webp.Width || image.Height != webp.Height || (webp.HasAlpha && !image.HasAlpha))
                throw new InvalidDataException("Inconsistent rendered photo geometry or alpha.");
            if (!await SaveAsync(image, purpose, fallback)) return false;
        }
        return await jobs.CompleteAsync(job, digest, variants, ct);

        async Task<bool> SaveAsync(RenderedPhotoVariant image, string purpose, string format)
        {
            var key = PhotoVariantRecipe.ObjectKey(job.Id, job.LeaseToken!.Value, purpose, format);
            if (!await jobs.RecordIntentAsync(job, key, ct)) return false;
            await storage.PutAsync(key, image, ct);
            variants.Add(new PhotoVariant
            {
                Id = Guid.NewGuid(), JobId = job.Id, PhotoId = job.PhotoId, SourceVersion = job.SourceVersion,
                RecipeVersion = job.RecipeVersion, Purpose = purpose, Format = format, ContentType = image.ContentType,
                ObjectKey = key, Width = image.Width, Height = image.Height, SizeBytes = image.Bytes.Length,
                Digest = Convert.ToHexStringLower(SHA256.HashData(image.Bytes))
            });
            return true;
        }
    }
}
