using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantPersistenceTests
{
    [Fact]
    public void MigrationIsAdditiveAndSnapshotMatchesModel()
    {
        using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261008211500_ReserveShopPhotoAttachments");
        Assert.Contains("CREATE TABLE \"PhotoVariantJobs\"", sql);
        Assert.Contains("CREATE TABLE \"PhotoVariants\"", sql);
        Assert.DoesNotContain("ALTER TABLE \"Photos\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("UNIQUE INDEX", sql);
    }

    [Fact]
    public async Task CleanupRetainsReadyReferencesFreshArtifactsAndActiveLeases()
    {
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        Add("ready", PhotoVariantJobStatus.Ready, now.AddDays(-2), null, referenced: true);
        Add("active", PhotoVariantJobStatus.Processing, now.AddDays(-2), now.AddMinutes(5));
        Add("fresh", PhotoVariantJobStatus.Failed, now.AddHours(-1), null);
        Add("orphan", PhotoVariantJobStatus.Failed, now.AddDays(-2), null);
        Add("deleted", PhotoVariantJobStatus.Cancelled, now.AddDays(-2), null, referenced: true);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cleanup = new PhotoVariantCleanup(db, null!, Options.Create(new PhotoVariantOptions()), NullLogger<PhotoVariantCleanup>.Instance);
        var keys = await cleanup.Candidates(now.AddDays(-1), now).Select(x => x.ObjectKey).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "deleted", "orphan" }, keys.Order());

        void Add(string key, PhotoVariantJobStatus status, DateTime created, DateTime? leaseUntil, bool referenced = false)
        {
            var job = new PhotoVariantJob { Id = Guid.NewGuid(), Status = status, LeaseUntilUtc = leaseUntil };
            db.PhotoVariantJobs.Add(job);
            db.PhotoVariantArtifacts.Add(new PhotoVariantArtifact { ObjectKey = key, JobId = job.Id, CreatedAtUtc = created });
            if (referenced) db.PhotoVariants.Add(new PhotoVariant { Id = Guid.NewGuid(), ObjectKey = key, JobId = job.Id });
        }
    }

    [Fact]
    public void ManifestCannotCommitPartialSetOrForeignAttemptObjects()
    {
        var job = new PhotoVariantJob { Id = Guid.NewGuid(), PhotoId = Guid.NewGuid(), LeaseToken = Guid.NewGuid(), RecipeVersion = 1, SourceVersion = "source" };
        var rows = Manifest(job);
        PhotoVariantJobStore.ValidateManifest(job, rows);
        Assert.Throws<InvalidOperationException>(() => PhotoVariantJobStore.ValidateManifest(job, rows[..5]));
        rows[0].ObjectKey = PhotoVariantRecipe.ObjectKey(job.Id, Guid.NewGuid(), "thumb", "webp");
        Assert.Throws<InvalidOperationException>(() => PhotoVariantJobStore.ValidateManifest(job, rows));
    }

    internal static PhotoVariant[] Manifest(PhotoVariantJob job) => PhotoVariantRecipe.Sizes.Keys
        .SelectMany(p => new[] { "webp", "jpeg" }.Select(f => new PhotoVariant
        {
            Id = Guid.NewGuid(), JobId = job.Id, PhotoId = job.PhotoId, SourceVersion = job.SourceVersion,
            RecipeVersion = job.RecipeVersion, Purpose = p, Format = f, ContentType = "image/" + f,
            Width = 100, Height = 200, SizeBytes = 123, Digest = new string('a', 64),
            ObjectKey = PhotoVariantRecipe.ObjectKey(job.Id, job.LeaseToken!.Value, p, f)
        })).ToArray();
}
