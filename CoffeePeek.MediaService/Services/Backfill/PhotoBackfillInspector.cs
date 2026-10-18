using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services.Backfill;

public sealed class PhotoBackfillInspector(MediaDbContext db, IPhotoBackfillObjectReader objects)
{
    // Shared across requests/scopes: a 40 MP decode must not multiply with concurrent operators.
    private static readonly SemaphoreSlim InspectionSlot = new(1, 1);

    public async Task<PhotoBackfillInspection[]> InspectAsync(PhotoBackfillCandidate[] candidates, bool verifyContent, CancellationToken ct)
    {
        if (candidates is not { Length: > 0 and <= 100 } || candidates.Any(x => x is null)
            || candidates.Select(x => x.PhotoId).Distinct().Count() != candidates.Length)
            throw new ArgumentException("Supply 1..100 different photo candidates.");
        var ids = candidates.Select(x => x.PhotoId).ToArray();
        var parents = candidates.Select(x => x.CheckInId).Distinct().ToArray();
        var photos = await db.Photos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var tombstones = (await db.CheckInPhotoTombstones.AsNoTracking().Where(t => parents.Contains(t.CheckInId))
            .Select(t => t.CheckInId).ToArrayAsync(ct)).ToHashSet();
        var jobs = await db.PhotoVariantJobs.AsNoTracking().Where(j => ids.Contains(j.PhotoId)
            && j.RecipeVersion == PhotoVariantRecipe.Version).ToArrayAsync(ct);
        var jobIds = jobs.Where(j => j.Status == PhotoVariantJobStatus.Ready).Select(j => j.Id).ToArray();
        var variants = await db.PhotoVariants.AsNoTracking().Where(v => jobIds.Contains(v.JobId)).ToArrayAsync(ct);
        var results = new List<PhotoBackfillInspection>(candidates.Length);
        foreach (var candidate in candidates)
        {
            photos.TryGetValue(candidate.PhotoId, out var photo);
            var failure = PhotoBackfillEligibility.Evaluate(candidate, photo, tombstones.Contains(candidate.CheckInId));
            var job = jobs.SingleOrDefault(j => j.PhotoId == candidate.PhotoId && j.SourceVersion == candidate.SourceVersion
                && j.SourceKey == candidate.StorageKey && j.OwnerId == candidate.OwnerId && j.CheckInId == candidate.CheckInId);
            long? variantBytes = null;
            if (failure is null && job is { Status: PhotoVariantJobStatus.Ready })
            {
                var manifest = variants.Where(v => v.JobId == job.Id).ToArray();
                if (CoffeePeek.Shared.Kernel.Media.CheckInPhotoVariantIdentity.IsDigest(job.SourceDigest)
                    && PhotoVariantManifest.Create(job, manifest).IsValid()
                    && manifest.All(v => v.PhotoId == candidate.PhotoId && v.SourceVersion == candidate.SourceVersion
                        && v.RecipeVersion == job.RecipeVersion
                        && v.Width <= PhotoVariantRecipe.Sizes[v.Purpose] && v.Height <= PhotoVariantRecipe.Sizes[v.Purpose]))
                    variantBytes = manifest.Sum(v => v.SizeBytes);
            }
            var content = failure is null ? await InspectSourceAsync(candidate.StorageKey, verifyContent, ct)
                : new PhotoBackfillContent("not_checked", null);
            if (content.Digest is not null && job?.SourceDigest is { } expected && content.Digest != expected)
            { content = content with { Status = "source_changed" }; variantBytes = null; }
            var mismatch = photo is not null && (candidate.DeclaredSizeBytes != photo.SizeBytes
                || content.Bytes.HasValue && content.Bytes.Value != photo.SizeBytes
                || !string.Equals(candidate.ContentType, photo.ContentType, StringComparison.OrdinalIgnoreCase));
            results.Add(new(candidate.PhotoId, failure, content.Status, candidate.DeclaredSizeBytes,
                photo?.SizeBytes, content.Bytes, mismatch, job?.Status.ToString(), variantBytes, PhotoVariantRecipe.Version));
        }
        return results.ToArray();
    }

    private async Task<PhotoBackfillContent> InspectSourceAsync(string key, bool verify, CancellationToken ct)
    {
        await InspectionSlot.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await using var source = await objects.OpenAsync(key, timeout.Token);
                return await PhotoBackfillContentInspector.InspectAsync(source, verify, timeout.Token);
            }
            catch (PhotoBackfillObjectMissingException) { return new("missing", null); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException) { return new("unavailable", null); }
        }
        finally { InspectionSlot.Release(); }
    }
}
