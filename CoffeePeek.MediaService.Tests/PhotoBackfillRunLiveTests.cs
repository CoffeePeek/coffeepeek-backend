using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public sealed class PhotoBackfillRunLiveTests(MediaVariantsLiveDatabase fixture) : IClassFixture<MediaVariantsLiveDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PhotoVariantJobStore Store(MediaDbContext db) => new(db, Options.Create(new PhotoVariantOptions()));

    [Fact]
    public async Task PopulatedUpgradePreservesExistingUploadPriorityAndReadyVariantIdentity()
    {
        RequireLive();
        var connection = await LivePostgresServer.CreateDatabaseAsync("mediaarchiveupgrade", Ct);
        try
        {
            await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync("20261018123301_AddPrivatePhotoVariants", Ct);
            var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
            var id = Guid.NewGuid();
            var source = PhotoVariantRecipe.SourceVersion(photo.Id, photo.StorageKey);
            var digest = new string('a', 64);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PhotoVariantJobs" ("Id", "PhotoId", "CheckInId", "OwnerId", "SourceKey", "SourceVersion",
                    "SourceDigest", "RecipeVersion", "Status", "Attempts", "NextAttemptAtUtc", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({id}, {photo.Id}, {photo.CheckInId}, {photo.OwnerId}, {photo.StorageKey}, {source},
                    {digest}, {PhotoVariantRecipe.Version}, 'Ready', 1, now(), now(), now())
                """, Ct);
            var ready = new PhotoVariantJob { Id = id, PhotoId = photo.Id, SourceVersion = source,
                RecipeVersion = PhotoVariantRecipe.Version, LeaseToken = Guid.NewGuid() };
            db.PhotoVariants.AddRange(PhotoVariantPersistenceTests.Manifest(ready));
            await db.SaveChangesAsync(Ct);
            await db.Database.MigrateAsync(Ct);
            var job = await db.PhotoVariantJobs.AsNoTracking().SingleAsync(j => j.Id == id, Ct);
            Assert.Equal(0, job.Priority); Assert.Null(job.BackfillRunId);
            Assert.Equal(PhotoVariantJobStatus.Ready, job.Status); Assert.Equal(digest, job.SourceDigest);
            Assert.Equal(photo.StorageKey, job.SourceKey); Assert.Equal(source, job.SourceVersion);
            Assert.Equal(photo.StorageKey, (await db.Photos.AsNoTracking().SingleAsync(Ct)).StorageKey);
            Assert.NotNull(await new PhotoVariantReads(db).FindAsync(photo.OwnerId, photo.CheckInId!.Value,
                photo.Id, source, PhotoVariantRecipe.Version, "thumb", "webp", Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task RestartUsesCheckpointAndRepeatedPassDoesNotDuplicateOrResetFailedJobs()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo();
        db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var source = new Source([Candidate(photo)]);
        var clock = new Clock();
        var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            Assert.True(run.Paused);
            Assert.Equal("paused", (await runs.AdvanceAsync(run.Id, Ct))!.Outcome);
            await runs.SetPausedAsync(run.Id, false, Ct);
            await runs.AdvanceAsync(run.Id, Ct);
            Assert.Equal(1, (await runs.GetAsync(run.Id, Ct))!.EnqueuedCount);
            Assert.Equal("rate_limited", (await runs.AdvanceAsync(run.Id, Ct))!.Outcome);
            await using var restart = fixture.Context();
            clock.Advance();
            var restarted = new PhotoBackfillRuns(restart, source, clock);
            await restarted.AdvanceAsync(run.Id, Ct);
            Assert.NotNull(source.Requests[^1].Cursor);
            var job = await db.PhotoVariantJobs.SingleAsync(j => j.PhotoId == photo.Id, Ct);
            await db.PhotoVariantJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(j => j.Status, PhotoVariantJobStatus.Failed).SetProperty(j => j.Attempts, 5), Ct);
            await runs.SetPausedAsync(run.Id, true, Ct);
            var repeat = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
            try
            {
                await runs.SetPausedAsync(repeat.Id, false, Ct);
                await runs.AdvanceAsync(repeat.Id, Ct);
                var completed = (await runs.GetAsync(repeat.Id, Ct))!;
                Assert.Equal(0, completed.EnqueuedCount); Assert.Equal(1, completed.ExistingCount);
                var unchanged = await db.PhotoVariantJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct);
                Assert.Equal(PhotoVariantJobStatus.Failed, unchanged.Status); Assert.Equal(5, unchanged.Attempts);
            }
            finally { await runs.SetPausedAsync(repeat.Id, true, Ct); }
        }
        finally { await runs.SetPausedAsync(run.Id, true, Ct); await Cancel(db, photo); }
    }

    [Fact]
    public async Task PauseStopsArchiveClaimsAndFreshUploadBypassesBacklogWithoutResettingJobs()
    {
        RequireLive();
        await using var db = fixture.Context();
        var archive = Photo(); var fresh = Photo();
        db.Photos.AddRange(archive, fresh); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var runs = new PhotoBackfillRuns(db, new Source([Candidate(archive)]), clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(run.Id, false, Ct); await runs.AdvanceAsync(run.Id, Ct);
            await runs.SetPausedAsync(run.Id, true, Ct);
            Assert.Null(await Store(db).ClaimAsync(Ct));
            await new PhotoVariantQueue(db).EnqueueAsync(fresh, Ct);
            await runs.SetPausedAsync(run.Id, false, Ct);
            var claimed = Assert.IsType<PhotoVariantJob>(await Store(db).ClaimAsync(Ct));
            Assert.Equal(fresh.Id, claimed.PhotoId); Assert.Equal(0, claimed.Priority);
            await Cancel(db, fresh);
            await runs.SetPausedAsync(run.Id, false, Ct);
            var archiveClaim = Assert.IsType<PhotoVariantJob>(await Store(db).ClaimAsync(Ct));
            Assert.Equal(archive.Id, archiveClaim.PhotoId); Assert.Equal(1, archiveClaim.Priority);
            await new PhotoVariantQueue(db).EnqueueAsync(archive, Ct);
            var promoted = await db.PhotoVariantJobs.AsNoTracking().SingleAsync(j => j.Id == archiveClaim.Id, Ct);
            Assert.Equal(0, promoted.Priority); Assert.Null(promoted.BackfillRunId);
            Assert.Equal(archiveClaim.Attempts, promoted.Attempts);
            Assert.Equal(archiveClaim.LeaseToken, promoted.LeaseToken);
            Assert.Equal(PhotoVariantJobStatus.Processing, promoted.Status);
        }
        finally { await runs.SetPausedAsync(run.Id, true, Ct); await Cancel(db, archive); await Cancel(db, fresh); }
    }

    [Fact]
    public async Task TombstonesPublicSourcesAndChangedBindingsAreBlockedWithoutMutatingOriginalMetadata()
    {
        RequireLive();
        await using var db = fixture.Context();
        var deleted = Photo(); var publicPhoto = Photo(); var replaced = Photo();
        publicPhoto.BucketType = BucketType.Shop;
        var oldBinding = Candidate(replaced);
        replaced.StorageKey = "upload/changed.jpg";
        db.Photos.AddRange(deleted, publicPhoto, replaced); await db.SaveChangesAsync(Ct);
        await new PhotoVariantQueue(db).CancelCheckInAsync(deleted.CheckInId!.Value, DateTime.UtcNow, Ct);
        var source = new Source([Candidate(deleted), Candidate(publicPhoto), oldBinding]);
        var clock = new Clock(); var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(run.Id, false, Ct); await runs.AdvanceAsync(run.Id, Ct);
            var status = (await runs.GetAsync(run.Id, Ct))!;
            Assert.Equal(3, status.BlockedCount); Assert.Equal(0, status.EnqueuedCount);
            var ids = new[] { deleted.Id, publicPhoto.Id, replaced.Id };
            Assert.False(await db.PhotoVariantJobs.AnyAsync(j => ids.Contains(j.PhotoId), Ct));
            var original = await db.Photos.AsNoTracking().SingleAsync(p => p.Id == publicPhoto.Id, Ct);
            Assert.Equal(BucketType.Shop, original.BucketType); Assert.Equal(publicPhoto.StorageKey, original.StorageKey);
            Assert.Equal(PhotoStatus.Confirmed, original.Status);
        }
        finally { await runs.SetPausedAsync(run.Id, true, Ct); }
    }

    [Fact]
    public async Task FailedCheckpointRollsBackEnqueueAndAnotherAttemptRecovers()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var source = new Source([Candidate(photo)]);
        var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(run.Id, false, Ct);
            await using (var failing = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>()
                             .UseNpgsql(fixture.ConnectionString).AddInterceptors(new RejectCheckpoint()).Options))
                await Assert.ThrowsAsync<InvalidOperationException>(() => new PhotoBackfillRuns(failing, source, clock).AdvanceAsync(run.Id, Ct));
            Assert.False(await db.PhotoVariantJobs.AnyAsync(j => j.PhotoId == photo.Id, Ct));
            Assert.Equal(0, (await runs.GetAsync(run.Id, Ct))!.ScannedCount);
            await runs.AdvanceAsync(run.Id, Ct);
            Assert.Equal(1, await db.PhotoVariantJobs.CountAsync(j => j.PhotoId == photo.Id, Ct));
        }
        finally { await runs.SetPausedAsync(run.Id, true, Ct); await Cancel(db, photo); }
    }

    [Fact]
    public async Task ConcurrentAdvancesSerializeAndOnlyOneRunCanProduceArchiveWork()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var source = new Source([Candidate(photo)]) { Hold = true };
        var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        var second = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(run.Id, false, Ct);
            Assert.Equal("another_run_active", (await runs.SetPausedAsync(second.Id, false, Ct))!.Outcome);
            var advance = runs.AdvanceAsync(run.Id, Ct);
            await source.Started.Task.WaitAsync(Ct);
            await using var competitorDb = fixture.Context();
            var competitor = new PhotoBackfillRuns(competitorDb, source, clock).AdvanceAsync(run.Id, Ct);
            await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
            source.Release.TrySetResult();
            Assert.Equal("advanced", (await advance)!.Outcome);
            Assert.Equal("rate_limited", (await competitor)!.Outcome);
            Assert.Single(source.Requests);
            Assert.Equal(1, await db.PhotoVariantJobs.CountAsync(j => j.PhotoId == photo.Id, Ct));
        }
        finally { source.Release.TrySetResult(); await runs.SetPausedAsync(run.Id, true, Ct); await Cancel(db, photo); }
    }

    [Fact]
    public async Task CompletedScanContinuesDrainingArchiveJobsAndRateGateDoesNotRestartTheScan()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var source = new Source([Candidate(photo)]) { EndPage = true };
        var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(run.Id, false, Ct); await runs.AdvanceAsync(run.Id, Ct);
            // Remaining phases are empty; each advance still observes the same server-side rate bound.
            source.Items = [];
            for (var i = 0; i < 3; i++) { clock.Advance(); await runs.AdvanceAsync(run.Id, Ct); }
            var completed = (await runs.GetAsync(run.Id, Ct))!;
            Assert.True(completed.ScanComplete); Assert.False(completed.Paused);
            var status = (await runs.StatusAsync(run.Id, Ct))!;
            Assert.Equal(1, status.Jobs[nameof(PhotoVariantJobStatus.Pending)]);
            Assert.False(status.GenerationDrained);
            Assert.Equal("scan_complete", (await runs.AdvanceAsync(run.Id, Ct))!.Outcome);
            Assert.Equal(photo.Id, (await Store(db).ClaimAsync(Ct))!.PhotoId);
            Assert.Equal(4, source.Requests.Count);
        }
        finally { await runs.SetPausedAsync(run.Id, true, Ct); await Cancel(db, photo); }
    }

    [Fact]
    public async Task PausedRunBacklogCannotBeAdoptedByAnotherRunOrMistakenForReadyCoverage()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var runs = new PhotoBackfillRuns(db, new Source([Candidate(photo)]), clock);
        var old = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        var next = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        try
        {
            await runs.SetPausedAsync(old.Id, false, Ct); await runs.AdvanceAsync(old.Id, Ct);
            await runs.SetPausedAsync(old.Id, true, Ct);
            Assert.Equal("previous_archive_backlog", (await runs.SetPausedAsync(next.Id, false, Ct))!.Outcome);
            Assert.Equal(1, (await runs.StatusAsync(old.Id, Ct))!.Jobs[nameof(PhotoVariantJobStatus.Pending)]);
            Assert.Null(await Store(db).ClaimAsync(Ct));
            await runs.SetPausedAsync(old.Id, false, Ct);
            Assert.Equal(photo.Id, (await Store(db).ClaimAsync(Ct))!.PhotoId);
            await Cancel(db, photo);
            await runs.SetPausedAsync(old.Id, true, Ct);
            Assert.Equal("resumed", (await runs.SetPausedAsync(next.Id, false, Ct))!.Outcome);
        }
        finally { await runs.SetPausedAsync(old.Id, true, Ct); await runs.SetPausedAsync(next.Id, true, Ct); await Cancel(db, photo); }
    }

    [Fact]
    public async Task ArchiveQueueBudgetBoundsPageSizeAndStopsProducingWhenFull()
    {
        RequireLive();
        await using var db = fixture.Context();
        var clock = new Clock();
        var originals = Enumerable.Range(0, PhotoBackfillRuns.MaxOutstandingArchiveJobs - 1).Select(_ => Photo()).ToArray();
        var newPhotos = new[] { Photo(), Photo(), Photo() };
        db.Photos.AddRange(originals.Concat(newPhotos)); await db.SaveChangesAsync(Ct);
        var source = new Source(newPhotos.Select(Candidate).ToArray());
        var runs = new PhotoBackfillRuns(db, source, clock);
        var run = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30), BatchSize: 100), Ct);
        try
        {
            db.PhotoVariantJobs.AddRange(originals.Select(p => new PhotoVariantJob
            {
                Id = Guid.NewGuid(), PhotoId = p.Id, CheckInId = p.CheckInId!.Value, OwnerId = p.OwnerId,
                SourceKey = p.StorageKey, SourceVersion = PhotoVariantRecipe.SourceVersion(p.Id, p.StorageKey),
                RecipeVersion = PhotoVariantRecipe.Version, Priority = 1, BackfillRunId = run.Id,
                Status = PhotoVariantJobStatus.Pending, NextAttemptAtUtc = clock.Now,
                CreatedAtUtc = clock.Now, UpdatedAtUtc = clock.Now
            }));
            await db.SaveChangesAsync(Ct);
            await runs.SetPausedAsync(run.Id, false, Ct); await runs.AdvanceAsync(run.Id, Ct);
            Assert.Equal(1, source.Requests.Single().Limit);
            Assert.Equal(PhotoBackfillRuns.MaxOutstandingArchiveJobs,
                await db.PhotoVariantJobs.CountAsync(j => j.BackfillRunId == run.Id, Ct));
            clock.Advance();
            Assert.Equal("queue_full", (await runs.AdvanceAsync(run.Id, Ct))!.Outcome);
            Assert.Single(source.Requests);
        }
        finally
        {
            await runs.SetPausedAsync(run.Id, true, Ct);
            await db.PhotoVariantJobs.Where(j => j.BackfillRunId == run.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(j => j.Status, PhotoVariantJobStatus.Cancelled), Ct);
        }
    }

    [Fact]
    public async Task ObsoleteRecipeBacklogCannotDeadlockTheCurrentRecipeOrChangeIssuedReadyManifests()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); var readyPhoto = Photo();
        db.Photos.AddRange(photo, readyPhoto); await db.SaveChangesAsync(Ct);
        var clock = new Clock(); var runs = new PhotoBackfillRuns(db, new Source([]), clock);
        var old = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        var current = await runs.CreateAsync(new(clock.Now, clock.Now.AddDays(-30)), Ct);
        // A previous binary issued this positive recipe before rolling back to the current binary.
        var issuedRecipe = PhotoVariantRecipe.Version + 1;
        try
        {
            await db.PhotoBackfillRuns.Where(r => r.Id == old.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.RecipeVersion, issuedRecipe).SetProperty(r => r.Paused, false), Ct);
            var oldJob = new PhotoVariantJob
            {
                Id = Guid.NewGuid(), PhotoId = photo.Id, CheckInId = photo.CheckInId!.Value, OwnerId = photo.OwnerId,
                SourceKey = photo.StorageKey, SourceVersion = PhotoVariantRecipe.SourceVersion(photo.Id, photo.StorageKey),
                RecipeVersion = issuedRecipe, Priority = 1, BackfillRunId = old.Id,
                Status = PhotoVariantJobStatus.Pending, NextAttemptAtUtc = clock.Now,
                CreatedAtUtc = clock.Now, UpdatedAtUtc = clock.Now
            };
            var readyJob = new PhotoVariantJob
            {
                Id = Guid.NewGuid(), PhotoId = readyPhoto.Id, CheckInId = readyPhoto.CheckInId!.Value, OwnerId = readyPhoto.OwnerId,
                SourceKey = readyPhoto.StorageKey, SourceVersion = PhotoVariantRecipe.SourceVersion(readyPhoto.Id, readyPhoto.StorageKey),
                SourceDigest = new string('b', 64), RecipeVersion = issuedRecipe, Priority = 1, BackfillRunId = old.Id,
                Status = PhotoVariantJobStatus.Ready, Attempts = 1, NextAttemptAtUtc = clock.Now,
                CreatedAtUtc = clock.Now, UpdatedAtUtc = clock.Now
            };
            var lease = Guid.NewGuid();
            var manifest = PhotoVariantPersistenceTests.Manifest(new PhotoVariantJob
            {
                Id = readyJob.Id, PhotoId = readyPhoto.Id, SourceVersion = readyJob.SourceVersion,
                RecipeVersion = issuedRecipe, LeaseToken = lease
            });
            foreach (var variant in manifest)
                variant.ObjectKey = $"v{issuedRecipe}/{readyJob.Id:N}/{lease:N}/{variant.Purpose}.{variant.Format}";
            db.PhotoVariantJobs.AddRange(oldJob, readyJob); db.PhotoVariants.AddRange(manifest);
            await db.SaveChangesAsync(Ct);
            Assert.True(PhotoVariantManifest.Create(readyJob, manifest).IsValid());
            var issued = manifest.Single(v => v.Purpose == "feed" && v.Format == "webp");
            var issuedUrl = CheckInPhotoVariantIdentity.Url(readyJob.CheckInId, readyPhoto.Id,
                issued.SourceVersion, issued.RecipeVersion, issued.Purpose, issued.Format);
            var issuedEtag = CheckInPhotoVariantIdentity.ETag(issued.SourceVersion, issued.RecipeVersion, issued.Digest);
            await runs.SetPausedAsync(old.Id, true, Ct);
            Assert.Equal("recipe_changed", (await runs.SetPausedAsync(old.Id, false, Ct))!.Outcome);
            Assert.Equal("resumed", (await runs.SetPausedAsync(current.Id, false, Ct))!.Outcome);
            Assert.Null(await Store(db).ClaimAsync(Ct));
            Assert.Equal(PhotoVariantJobStatus.Pending,
                (await db.PhotoVariantJobs.AsNoTracking().SingleAsync(j => j.Id == oldJob.Id, Ct)).Status);
            var preserved = Assert.IsType<PhotoVariant>(await new PhotoVariantReads(db).FindAsync(readyPhoto.OwnerId,
                readyJob.CheckInId, readyPhoto.Id, issued.SourceVersion, issuedRecipe, "feed", "webp", Ct));
            Assert.Equal(issued.ObjectKey, preserved.ObjectKey);
            Assert.Equal(issuedUrl, CheckInPhotoVariantIdentity.Url(readyJob.CheckInId, readyPhoto.Id,
                preserved.SourceVersion, preserved.RecipeVersion, preserved.Purpose, preserved.Format));
            Assert.Equal(issuedEtag, CheckInPhotoVariantIdentity.ETag(preserved.SourceVersion, preserved.RecipeVersion, preserved.Digest));
            Assert.Equal(6, await db.PhotoVariants.CountAsync(v => v.JobId == readyJob.Id, Ct));
            Assert.Equal(1, (await runs.StatusAsync(old.Id, Ct))!.Jobs[nameof(PhotoVariantJobStatus.Ready)]);
        }
        finally { await runs.SetPausedAsync(old.Id, true, Ct); await runs.SetPausedAsync(current.Id, true, Ct); await Cancel(db, photo); await Cancel(db, readyPhoto); }
    }

    private static PhotoMetadata Photo()
    {
        var id = Guid.NewGuid();
        return new() { Id = id, CheckInId = Guid.NewGuid(), OwnerId = Guid.NewGuid(),
            Status = PhotoStatus.Confirmed, BucketType = BucketType.CheckIn, StorageKey = $"check-ins/{id:N}.jpg",
            FileName = "test.jpg", ContentType = "image/jpeg", SizeBytes = 123, UploadedAt = DateTime.UtcNow.AddDays(-1), OwnerType = OwnerType.Shop };
    }

    private static PhotoBackfillCandidate Candidate(PhotoMetadata photo) => new(photo.Id, photo.CheckInId!.Value,
        photo.OwnerId, photo.StorageKey, PhotoVariantRecipe.SourceVersion(photo.Id, photo.StorageKey),
        CheckInKind.Shop, false, photo.UploadedAt, photo.SizeBytes, photo.ContentType);

    private static Task Cancel(MediaDbContext db, PhotoMetadata photo) => new PhotoVariantQueue(db)
        .CancelCheckInAsync(photo.CheckInId!.Value, DateTime.UtcNow, Ct);

    private static void RequireLive() => Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; private set; } = DateTime.UtcNow;
        public override DateTimeOffset GetUtcNow() => new(Now);
        public void Advance() => Now = Now.AddSeconds(6);
    }

    private sealed class Source(PhotoBackfillCandidate[] items) : IPhotoBackfillCandidateSource
    {
        public PhotoBackfillCandidate[] Items { get; set; } = items;
        public List<PhotoBackfillPageRequest> Requests { get; } = [];
        public bool Hold { get; init; }
        public bool EndPage { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest request, CancellationToken ct)
        {
            Requests.Add(request); Started.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            if (request.Cursor is not null) return new([], null);
            var selected = Items.OrderByDescending(x => x.SortAtUtc).ThenByDescending(x => x.CheckInId)
                .ThenByDescending(x => x.PhotoId).Take(request.Limit).ToArray();
            return new(selected, EndPage || selected.Length == 0 ? null
                : new PhotoBackfillCursor(selected[^1].SortAtUtc, selected[^1].CheckInId, selected[^1].PhotoId));
        }
    }

    private sealed class RejectCheckpoint : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected checkpoint failure after enqueue.");
    }
}
