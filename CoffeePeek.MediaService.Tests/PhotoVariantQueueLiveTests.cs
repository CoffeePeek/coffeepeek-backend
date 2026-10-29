using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public sealed class PhotoVariantQueueLiveTests(MediaVariantsLiveDatabase fixture) : IClassFixture<MediaVariantsLiveDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PhotoVariantJobStore Store(MediaDbContext db) => new(db, Options.Create(new PhotoVariantOptions()));

    [Fact]
    public async Task PopulatedUpgradePreservesOriginalMetadata()
    {
        RequireLive();
        var connection = await LivePostgresServer.CreateDatabaseAsync("mediavariantupgrade", Ct);
        try
        {
            await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync("20261008211500_ReserveShopPhotoAttachments", Ct);
            var photo = Photo();
            // Seed only historical columns: the current EF model also contains newer additions.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Photos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes",
                    "UploadedAt", "Status", "BucketType", "OwnerType", "OwnerId", "CheckInId")
                VALUES ({photo.Id}, {photo.FileName}, {photo.ContentType}, {photo.StorageKey}, {photo.SizeBytes},
                    {photo.UploadedAt}, {photo.Status.ToString()}, {photo.BucketType.ToString()},
                    {photo.OwnerType.ToString()}, {photo.OwnerId}, {photo.CheckInId})
                """, Ct);
            await db.Database.MigrateAsync(Ct);
            db.ChangeTracker.Clear();
            var loaded = await db.Photos.SingleAsync(Ct);
            Assert.Equal(photo.StorageKey, loaded.StorageKey); Assert.Equal(photo.CheckInId, loaded.CheckInId);
            Assert.Equal(PhotoStatus.Confirmed, loaded.Status);
            Assert.Null(loaded.ShelfBagId);
            Assert.Empty(await db.PhotoVariantJobs.ToArrayAsync(Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task EnqueueIsIdempotentAndRollsBackWithOwningTransaction()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            await new PhotoVariantQueue(db).EnqueueAsync(photo, Ct);
            await transaction.RollbackAsync(Ct);
        }
        Assert.False(await db.PhotoVariantJobs.AnyAsync(x => x.PhotoId == photo.Id, Ct));
        await new PhotoVariantQueue(db).EnqueueAsync(photo, Ct);
        await new PhotoVariantQueue(db).EnqueueAsync(photo, Ct);
        Assert.Equal(1, await db.PhotoVariantJobs.CountAsync(x => x.PhotoId == photo.Id, Ct));
        // Keep the shared fixture's queue empty for the following test.
        await new PhotoVariantQueue(db).CancelCheckInAsync(photo.CheckInId!.Value, DateTime.UtcNow, Ct);
    }

    [Fact]
    public async Task LeaseExpiryFencesOldWorkerAndReadyIsAtomic()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = await EnqueueAsync(db);
        var first = Assert.IsType<PhotoVariantJob>(await Store(db).ClaimAsync(Ct));
        await using var secondDb = fixture.Context();
        Assert.Null(await Store(secondDb).ClaimAsync(Ct));
        Assert.True(await Store(db).RecordIntentAsync(first, "old-attempt", Ct));
        await db.PhotoVariantJobs.Where(x => x.Id == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)), Ct);
        var second = Assert.IsType<PhotoVariantJob>(await Store(secondDb).ClaimAsync(Ct));
        Assert.NotEqual(first.LeaseToken, second.LeaseToken); Assert.Equal(2, second.Attempts);
        foreach (var row in PhotoVariantPersistenceTests.Manifest(second))
            Assert.True(await Store(secondDb).RecordIntentAsync(second, row.ObjectKey, Ct));
        Assert.False(await Store(db).CompleteAsync(first, new string('a', 64), PhotoVariantPersistenceTests.Manifest(first), Ct));
        Assert.False(await Store(db).RecordIntentAsync(first, "stale-attempt", Ct));
        await Store(db).FailAsync(first, "render_unavailable", false, Ct);
        Assert.True(await Store(secondDb).CompleteAsync(second, new string('b', 64), PhotoVariantPersistenceTests.Manifest(second), Ct));
        Assert.Equal(6, await db.PhotoVariants.CountAsync(x => x.PhotoId == photo.Id, Ct));
        Assert.Equal(PhotoVariantJobStatus.Ready, (await db.PhotoVariantJobs.AsNoTracking().SingleAsync(x => x.Id == first.Id, Ct)).Status);
        photo.Status = PhotoStatus.Deleted;
        await new PhotoVariantQueue(db).CancelPhotoAsync(photo.Id, Ct);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(PhotoVariantJobStatus.Cancelled, (await db.PhotoVariantJobs.AsNoTracking().SingleAsync(x => x.Id == first.Id, Ct)).Status);
        await new PhotoVariantQueue(db).EnqueueAsync(photo, Ct);
        Assert.Null(await Store(db).ClaimAsync(Ct));
    }

    [Fact]
    public async Task DeletionBeforeAttachmentAndDuringRenderingCannotResurrectVariants()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        var queue = new PhotoVariantQueue(db);
        await queue.CancelCheckInAsync(photo.CheckInId!.Value, DateTime.UtcNow, Ct);
        await queue.EnqueueAsync(photo, Ct);
        Assert.False(await db.PhotoVariantJobs.AnyAsync(x => x.PhotoId == photo.Id, Ct));
        var active = await EnqueueAsync(db);
        var job = Assert.IsType<PhotoVariantJob>(await Store(db).ClaimAsync(Ct));
        await queue.CancelCheckInAsync(active.CheckInId!.Value, DateTime.UtcNow, Ct);
        Assert.False(await Store(db).CompleteAsync(job, new string('a', 64), PhotoVariantPersistenceTests.Manifest(job), Ct));
        await queue.EnqueueAsync(active, Ct);
        Assert.Null(await Store(db).ClaimAsync(Ct));
        Assert.Empty(await db.PhotoVariants.Where(x => x.PhotoId == active.Id).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task RetryAndLastAttemptCrashHaveBoundedTerminalStates()
    {
        RequireLive();
        await using var db = fixture.Context();
        await EnqueueAsync(db);
        var store = Store(db);
        var job = Assert.IsType<PhotoVariantJob>(await store.ClaimAsync(Ct));
        await store.FailAsync(job, "render_unavailable", false, Ct);
        Assert.Null(await store.ClaimAsync(Ct));
        await db.PhotoVariantJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)), Ct);
        var retry = Assert.IsType<PhotoVariantJob>(await store.ClaimAsync(Ct));
        Assert.Equal(2, retry.Attempts);
        await db.PhotoVariantJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)).SetProperty(x => x.Attempts, 5), Ct);
        Assert.Null(await store.ClaimAsync(Ct));
        Assert.Equal(PhotoVariantJobStatus.Failed, (await db.PhotoVariantJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, Ct)).Status);
    }

    private static async Task<PhotoMetadata> EnqueueAsync(MediaDbContext db)
    {
        var photo = Photo(); db.Photos.Add(photo); await db.SaveChangesAsync(Ct);
        await new PhotoVariantQueue(db).EnqueueAsync(photo, Ct);
        return photo;
    }

    [Fact]
    public async Task DeleteHoldingRowLockWinsAgainstConcurrentReadyCommit()
    {
        RequireLive();
        await using var db = fixture.Context();
        var photo = await EnqueueAsync(db);
        var job = Assert.IsType<PhotoVariantJob>(await Store(db).ClaimAsync(Ct));
        foreach (var row in PhotoVariantPersistenceTests.Manifest(job))
            Assert.True(await Store(db).RecordIntentAsync(job, row.ObjectKey, Ct));
        await using var deletion = await db.Database.BeginTransactionAsync(Ct);
        await new PhotoVariantQueue(db).CancelCheckInAsync(photo.CheckInId!.Value, DateTime.UtcNow, Ct);
        await using var completingDb = fixture.Context();
        var completion = Store(completingDb).CompleteAsync(job, new string('a', 64), PhotoVariantPersistenceTests.Manifest(job), Ct);
        await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
        await deletion.CommitAsync(Ct);
        Assert.False(await completion);
        Assert.Empty(await db.PhotoVariants.Where(x => x.PhotoId == photo.Id).ToArrayAsync(Ct));
    }

    private static PhotoMetadata Photo()
    {
        var id = Guid.NewGuid();
        return new PhotoMetadata
        {
            Id = id, CheckInId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Status = PhotoStatus.Confirmed,
            BucketType = BucketType.CheckIn, StorageKey = $"check-ins/{id:N}.jpg", ContentType = "image/jpeg",
            FileName = "test.jpg", SizeBytes = 123, UploadedAt = DateTime.UtcNow, OwnerType = OwnerType.Shop
        };
    }

    private static void RequireLive() => Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
}
