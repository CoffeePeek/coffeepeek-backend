using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Services.Backfill;

/// <summary>Operator-driven archive producer. Each advance is bounded and commits jobs with its checkpoint.</summary>
public sealed class PhotoBackfillRuns(MediaDbContext db, IPhotoBackfillCandidateSource candidates,
    TimeProvider? clock = null)
{
    public const int MaxOutstandingArchiveJobs = 200;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const long ProducerLock = 743801836447201L;

    public async Task<PhotoBackfillRun> CreateAsync(CreatePhotoBackfillRun request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        ValidateCreate(request, now);
        var run = new PhotoBackfillRun
        {
            Id = Guid.NewGuid(), BeforeUtc = request.BeforeUtc, RecentSinceUtc = request.RecentSinceUtc,
            RecipeVersion = PhotoVariantRecipe.Version, Phase = PhotoBackfillPhase.RecentPublicShop,
            BatchSize = request.BatchSize, MinBatchIntervalSeconds = request.MinBatchIntervalSeconds,
            CreatedAtUtc = now, UpdatedAtUtc = now, NextBatchAtUtc = now, Paused = true
        };
        db.PhotoBackfillRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    internal static void ValidateCreate(CreatePhotoBackfillRun request, DateTime now)
    {
        if (request.BeforeUtc == default || request.RecentSinceUtc == default
            || request.BeforeUtc.Kind != DateTimeKind.Utc || request.RecentSinceUtc.Kind != DateTimeKind.Utc
            || request.BeforeUtc > now || request.RecentSinceUtc >= request.BeforeUtc
            || request.BatchSize is < 1 or > 100 || request.MinBatchIntervalSeconds is < 5 or > 3600)
            throw new ArgumentException("A run requires past UTC cutoffs, 1..100 photos and an interval of 5..3600 seconds.");
    }

    public Task<PhotoBackfillRun?> GetAsync(Guid id, CancellationToken ct) =>
        db.PhotoBackfillRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PhotoBackfillRunStatus?> StatusAsync(Guid id, CancellationToken ct)
    {
        var run = await GetAsync(id, ct);
        if (run is null) return null;
        var counts = await db.PhotoVariantJobs.AsNoTracking().Where(j => j.BackfillRunId == id)
            .GroupBy(j => j.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToArrayAsync(ct);
        var jobs = Enum.GetValues<PhotoVariantJobStatus>().ToDictionary(s => s.ToString(), _ => 0);
        foreach (var count in counts) jobs[count.Status.ToString()] = count.Count;
        return new(run, jobs, run.ScanComplete && counts.All(c => c.Status is not
            (PhotoVariantJobStatus.Pending or PhotoVariantJobStatus.Processing or PhotoVariantJobStatus.RetryScheduled)));
    }

    public async Task<PhotoBackfillAdvanceResult?> SetPausedAsync(Guid id, bool paused, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize activation globally; the partial unique index is the second line of protection.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({ProducerLock})", ct);
        var run = await LockRunAsync(id, ct);
        if (run is null) return null;
        if (!paused && run.RecipeVersion != PhotoVariantRecipe.Version) return new("recipe_changed", run);
        if (!paused && await db.PhotoBackfillRuns.AnyAsync(x => x.Id != id && !x.Paused, ct))
            return new("another_run_active", run);
        if (!paused && await db.PhotoVariantJobs.AnyAsync(j => j.Priority == 1 && j.BackfillRunId != id
                && j.RecipeVersion == PhotoVariantRecipe.Version
                && (j.Status == PhotoVariantJobStatus.Pending || j.Status == PhotoVariantJobStatus.Processing
                    || j.Status == PhotoVariantJobStatus.RetryScheduled), ct))
            return new("previous_archive_backlog", run);
        run.Paused = paused;
        run.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(paused ? "paused" : "resumed", run);
    }

    public async Task<PhotoBackfillAdvanceResult?> AdvanceAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var run = await LockRunAsync(id, ct);
        if (run is null) return null;
        var now = time.GetUtcNow().UtcDateTime;
        if (run.Paused) return new("paused", run);
        if (run.ScanComplete) return new("scan_complete", run);
        if (run.RecipeVersion != PhotoVariantRecipe.Version) return new("recipe_changed", run);
        if (now < run.NextBatchAtUtc) return new("rate_limited", run);

        var outstanding = await db.PhotoVariantJobs.CountAsync(j => j.Priority == 1
            && j.RecipeVersion == PhotoVariantRecipe.Version
            && (j.Status == PhotoVariantJobStatus.Pending || j.Status == PhotoVariantJobStatus.RetryScheduled
                || j.Status == PhotoVariantJobStatus.Processing), ct);
        var limit = Math.Min(run.BatchSize, MaxOutstandingArchiveJobs - outstanding);
        if (limit <= 0) return new("queue_full", run);

        var cursor = Cursor(run);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var page = await candidates.ReadAsync(new PhotoBackfillPageRequest(run.Phase,
            run.BeforeUtc, run.RecentSinceUtc, cursor, limit), timeout.Token);
        ValidatePage(page, cursor, limit, run.BeforeUtc);
        var ids = page.Items.Select(x => x.PhotoId).ToArray();
        var photos = await db.Photos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var checkInIds = page.Items.Select(x => x.CheckInId).Distinct().ToArray();
        var tombstones = (await db.CheckInPhotoTombstones.AsNoTracking().Where(t => checkInIds.Contains(t.CheckInId))
            .Select(t => t.CheckInId).ToArrayAsync(ct)).ToHashSet();
        foreach (var candidate in page.Items)
        {
            run.ScannedCount++;
            photos.TryGetValue(candidate.PhotoId, out var photo);
            if (PhotoBackfillEligibility.Evaluate(candidate, photo, tombstones.Contains(candidate.CheckInId)) is not null)
            {
                run.BlockedCount++;
                continue;
            }
            // Recheck current binding and deletion fences in the same statement as insertion.
            // Conflicts deliberately preserve Failed/Cancelled/Ready states and their attempts.
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PhotoVariantJobs"
                    ("Id", "PhotoId", "CheckInId", "OwnerId", "SourceKey", "SourceVersion", "RecipeVersion",
                     "Status", "Attempts", "NextAttemptAtUtc", "CreatedAtUtc", "UpdatedAtUtc", "Priority", "BackfillRunId")
                SELECT {Guid.NewGuid()}, p."Id", p."CheckInId", p."OwnerId", p."StorageKey",
                    {candidate.SourceVersion}, {run.RecipeVersion}, 'Pending', 0, now(), now(), now(), 1, {run.Id}
                FROM "Photos" p WHERE p."Id" = {candidate.PhotoId} AND p."CheckInId" = {candidate.CheckInId}
                    AND p."OwnerId" = {candidate.OwnerId} AND p."StorageKey" = {candidate.StorageKey}
                    AND p."Status" = 'Confirmed' AND p."BucketType" = 'CheckIn' AND p."ShopAttachmentId" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId" = p."CheckInId")
                ON CONFLICT ("PhotoId", "SourceVersion", "RecipeVersion") DO NOTHING
                """, ct);
            if (inserted == 1) run.EnqueuedCount++;
            else if (await db.PhotoVariantJobs.AnyAsync(j => j.PhotoId == candidate.PhotoId
                         && j.SourceVersion == candidate.SourceVersion && j.RecipeVersion == run.RecipeVersion, ct))
                run.ExistingCount++;
            else run.BlockedCount++;
        }
        if (page.NextCursor is { } next)
        {
            run.CursorSortAtUtc = next.SortAtUtc;
            run.CursorCheckInId = next.CheckInId;
            run.CursorPhotoId = next.PhotoId;
        }
        else
        {
            run.CursorSortAtUtc = null;
            run.CursorCheckInId = null;
            run.CursorPhotoId = null;
            var nextPhase = NextPhase(run.Phase);
            if (nextPhase is { } phase) run.Phase = phase;
            else run.ScanComplete = true;
        }
        // Time spent reading a page cannot consume the interval and permit a burst on return.
        now = time.GetUtcNow().UtcDateTime;
        run.NextBatchAtUtc = now.AddSeconds(run.MinBatchIntervalSeconds);
        run.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(run.ScanComplete ? "scan_complete" : "advanced", run);
    }

    private async Task<PhotoBackfillRun?> LockRunAsync(Guid id, CancellationToken ct)
    {
        // Reload after acquiring the row lock, even when this scoped context created the run.
        var run = (await db.PhotoBackfillRuns.FromSqlInterpolated($"""
            SELECT * FROM "PhotoBackfillRuns" WHERE "Id" = {id} FOR UPDATE
            """).AsNoTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (run is null) return null;
        var tracked = db.ChangeTracker.Entries<PhotoBackfillRun>().FirstOrDefault(x => x.Entity.Id == id);
        if (tracked is not null) tracked.State = EntityState.Detached;
        db.PhotoBackfillRuns.Attach(run);
        return run;
    }

    private static PhotoBackfillCursor? Cursor(PhotoBackfillRun run) => run.CursorSortAtUtc is { } date
        && run.CursorCheckInId is { } checkIn && run.CursorPhotoId is { } photo
            ? new(date, checkIn, photo) : null;

    internal static PhotoBackfillPhase? NextPhase(PhotoBackfillPhase phase) => phase switch
    {
        PhotoBackfillPhase.RecentPublicShop => PhotoBackfillPhase.OtherShop,
        PhotoBackfillPhase.OtherShop => PhotoBackfillPhase.Home,
        PhotoBackfillPhase.Home => PhotoBackfillPhase.PublicShopArchive,
        PhotoBackfillPhase.PublicShopArchive => null,
        _ => throw new InvalidDataException("Unknown archive phase.")
    };

    internal static void ValidatePage(PhotoBackfillPage page, PhotoBackfillCursor? cursor, int limit, DateTime before)
    {
        if (page.Items is null || page.Items.Length > limit || (page.Items.Length == 0 && page.NextCursor is not null)
            || page.Items.Select(x => x.PhotoId).Distinct().Count() != page.Items.Length)
            throw new InvalidDataException("Invalid archive candidate page.");
        foreach (var item in page.Items)
        {
            var next = new PhotoBackfillCursor(item.SortAtUtc, item.CheckInId, item.PhotoId);
            if (item.SortAtUtc.Kind != DateTimeKind.Utc || item.SortAtUtc > before || item.CheckInId == Guid.Empty
                || item.PhotoId == Guid.Empty || (cursor is not null && Compare(next, cursor) >= 0))
                throw new InvalidDataException("Archive candidate cursor did not advance.");
            cursor = next;
        }
        if (page.NextCursor is not null && page.NextCursor != cursor)
            throw new InvalidDataException("Archive checkpoint must identify the final candidate.");
    }

    private static int Compare(PhotoBackfillCursor left, PhotoBackfillCursor right)
    {
        var compared = left.SortAtUtc.CompareTo(right.SortAtUtc);
        if (compared == 0) compared = left.CheckInId.CompareTo(right.CheckInId);
        if (compared == 0) compared = left.PhotoId.CompareTo(right.PhotoId);
        return compared;
    }
}
