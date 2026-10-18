using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantCleanup(MediaDbContext db, PhotoVariantStorage storage,
    IOptions<PhotoVariantOptions> options, ILogger<PhotoVariantCleanup> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var before = DateTime.UtcNow.AddHours(-options.Value.CleanupGraceHours);
        var now = DateTime.UtcNow;
        var candidates = await Candidates(before, now).OrderBy(x => x.CreatedAtUtc).Take(50).ToArrayAsync(ct);
        if (options.Value.CleanupDryRun)
        {
            logger.LogInformation("Variant cleanup dry run: {Count} eligible artifacts in this batch.", candidates.Length);
            return candidates.Length;
        }
        await storage.RequirePrivateAsync(ct);
        foreach (var artifact in candidates)
        {
            // Recheck before deletion. Expired attempt tokens can never commit Ready or be renewed.
            if (!await Candidates(before, DateTime.UtcNow).AnyAsync(x => x.ObjectKey == artifact.ObjectKey, ct)) continue;
            await storage.DeleteAsync(artifact.ObjectKey, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.PhotoVariants.Where(x => x.ObjectKey == artifact.ObjectKey).ExecuteDeleteAsync(ct);
            await db.PhotoVariantArtifacts.Where(x => x.ObjectKey == artifact.ObjectKey).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return candidates.Length;
    }

    internal IQueryable<PhotoVariantArtifact> Candidates(DateTime before, DateTime now) =>
        from artifact in db.PhotoVariantArtifacts.AsNoTracking()
        join job in db.PhotoVariantJobs on artifact.JobId equals job.Id
        where artifact.CreatedAtUtc < before && (job.LeaseUntilUtc == null || job.LeaseUntilUtc <= now)
          && (job.Status == PhotoVariantJobStatus.Cancelled
              || !db.PhotoVariants.Any(v => v.ObjectKey == artifact.ObjectKey))
        select artifact;
}
