using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantJobStore(MediaDbContext db, IOptions<PhotoVariantOptions> options,
    IDbContextOutbox? outbox = null)
{
    public async Task<PhotoVariantJob?> ClaimAsync(CancellationToken ct)
    {
        await ReconcilePendingAsync(ct);
        var maxAttempts = options.Value.MaxAttempts;
        // A crash on the last attempt must not leave an immortal Processing row.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PhotoVariantJobs" SET "Status" = 'Failed', "FailureCode" = 'attempts_exhausted',
                "LeaseToken" = NULL, "LeaseUntilUtc" = NULL, "UpdatedAtUtc" = now()
            WHERE "Status" = 'Processing' AND "LeaseUntilUtc" <= now() AND "Attempts" >= {maxAttempts}
            """, ct);
        var token = Guid.NewGuid();
        var duration = PhotoVariantOptions.LeaseDuration;
        var rows = await db.PhotoVariantJobs.FromSqlInterpolated($"""
            WITH candidate AS (
                SELECT j."Id" FROM "PhotoVariantJobs" j
                JOIN "Photos" p ON p."Id" = j."PhotoId"
                WHERE j."RecipeVersion" = {PhotoVariantRecipe.Version} AND j."Attempts" < {maxAttempts}
                  AND ((j."Status" IN ('Pending', 'RetryScheduled') AND j."NextAttemptAtUtc" <= now())
                    OR (j."Status" = 'Processing' AND j."LeaseUntilUtc" <= now()))
                  AND p."Status" = 'Confirmed' AND p."BucketType" = 'CheckIn'
                  AND p."CheckInId" = j."CheckInId" AND p."OwnerId" = j."OwnerId"
                  AND p."StorageKey" = j."SourceKey" AND p."ShopAttachmentId" IS NULL
                  AND NOT EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId" = j."CheckInId")
                ORDER BY j."NextAttemptAtUtc", j."Id" FOR UPDATE OF j SKIP LOCKED LIMIT 1
            )
            UPDATE "PhotoVariantJobs" j SET "Status" = 'Processing', "LeaseToken" = {token},
                "LeaseUntilUtc" = now() + {duration}, "Attempts" = j."Attempts" + 1, "UpdatedAtUtc" = now()
            FROM candidate WHERE j."Id" = candidate."Id" RETURNING j.*
            """).AsNoTracking().ToArrayAsync(ct);
        return rows.SingleOrDefault();
    }

    private Task ReconcilePendingAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        UPDATE "PhotoVariantJobs" j SET "Status" = 'Cancelled', "LeaseToken" = NULL,
            "LeaseUntilUtc" = NULL, "UpdatedAtUtc" = now()
        WHERE j."Status" IN ('Pending', 'RetryScheduled', 'Processing') AND (
            EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId" = j."CheckInId")
            OR NOT EXISTS (SELECT 1 FROM "Photos" p WHERE p."Id" = j."PhotoId"
                AND p."Status" = 'Confirmed' AND p."BucketType" = 'CheckIn'
                AND p."CheckInId" = j."CheckInId" AND p."OwnerId" = j."OwnerId"
                AND p."StorageKey" = j."SourceKey" AND p."ShopAttachmentId" IS NULL))
        """, ct);

    public async Task<bool> RecordIntentAsync(PhotoVariantJob job, string objectKey, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PhotoVariantArtifacts" ("ObjectKey", "JobId", "LeaseToken", "CreatedAtUtc")
            SELECT {objectKey}, j."Id", {job.LeaseToken}, now() FROM "PhotoVariantJobs" j
            WHERE j."Id" = {job.Id} AND j."Status" = 'Processing' AND j."LeaseToken" = {job.LeaseToken}
              AND j."LeaseUntilUtc" > now()
              AND NOT EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId" = j."CheckInId")
            ON CONFLICT ("ObjectKey") DO NOTHING
            """, ct) == 1;

    public async Task<bool> CompleteAsync(PhotoVariantJob job, string sourceDigest,
        IReadOnlyCollection<PhotoVariant> variants, CancellationToken ct)
    {
        ValidateManifest(job, variants);
        if (sourceDigest.Length != 64 || !sourceDigest.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Missing verified source digest.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var keys = variants.Select(x => x.ObjectKey).ToArray();
        if (await db.PhotoVariantArtifacts.CountAsync(x => x.JobId == job.Id && x.LeaseToken == job.LeaseToken
                && keys.Contains(x.ObjectKey), ct) != variants.Count)
            return false;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PhotoVariantJobs" j SET "Status" = 'Ready', "SourceDigest" = {sourceDigest},
                "LeaseToken" = NULL, "LeaseUntilUtc" = NULL, "FailureCode" = NULL, "UpdatedAtUtc" = now()
            FROM "Photos" p WHERE j."Id" = {job.Id} AND j."Status" = 'Processing'
              AND j."LeaseToken" = {job.LeaseToken} AND j."LeaseUntilUtc" > now()
              AND p."Id" = j."PhotoId" AND p."Status" = 'Confirmed' AND p."BucketType" = 'CheckIn'
              AND p."CheckInId" = j."CheckInId" AND p."OwnerId" = j."OwnerId"
              AND p."StorageKey" = j."SourceKey" AND p."ShopAttachmentId" IS NULL
              AND NOT EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId" = j."CheckInId")
            """, ct);
        if (changed == 0) return false;
        db.PhotoVariants.AddRange(variants);
        // Production DI enrolls this context before publishing. The envelope and Ready manifest
        // commit in the same transaction; transport dispatch happens only after commit.
        if (outbox is not null) await outbox.PublishAsync(PhotoVariantManifest.Create(job, variants));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (outbox is not null) await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    internal static void ValidateManifest(PhotoVariantJob job, IReadOnlyCollection<PhotoVariant> variants)
    {
        if (job.LeaseToken is not { } lease || variants.Count != 6
            || variants.Any(x => x.JobId != job.Id || x.PhotoId != job.PhotoId
                || x.SourceVersion != job.SourceVersion || x.RecipeVersion != job.RecipeVersion
                || x.ObjectKey != PhotoVariantRecipe.ObjectKey(job.Id, lease, x.Purpose, x.Format)
                || x.ContentType != "image/" + x.Format || x.Digest.Length != 64 || !x.Digest.All(Uri.IsHexDigit)
                || x.Width <= 0 || x.Height <= 0 || x.Width > PhotoVariantRecipe.Sizes[x.Purpose]
                || x.Height > PhotoVariantRecipe.Sizes[x.Purpose]
                || x.SizeBytes <= 0 || x.SizeBytes > PhotoVariantOptions.MaxResultBytes)
            || PhotoVariantRecipe.Sizes.Keys.Any(p => variants.Count(x => x.Purpose == p && x.Format == "webp") != 1
                || variants.Count(x => x.Purpose == p && x.Format is "jpeg" or "png") != 1))
            throw new InvalidOperationException("Incomplete or mismatched photo variant manifest.");
    }

    public Task FailAsync(PhotoVariantJob job, string code, bool permanent, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PhotoVariantJobs" SET "Status" = {(permanent || job.Attempts >= options.Value.MaxAttempts ? "Failed" : "RetryScheduled")},
                "FailureCode" = {code}, "LeaseToken" = NULL, "LeaseUntilUtc" = NULL,
                "NextAttemptAtUtc" = now() + {TimeSpan.FromSeconds(Math.Min(900, 15 * Math.Pow(2, job.Attempts)))},
                "UpdatedAtUtc" = now()
            WHERE "Id" = {job.Id} AND "Status" = 'Processing' AND "LeaseToken" = {job.LeaseToken}
            """, ct);
}
