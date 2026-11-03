using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class CollectionCoverCleanupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ExpiredClaimIsConfirmedOnlyIfCurrentDeletedOnlyIfUnreferencedAndRetainedOnOutage(bool? current)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var photo = Photo("collection-covers/sealed/photo.jpg", DateTime.UtcNow.AddHours(-30));
        photo.OriginalStorageKey = "collection-covers/upload.jpg";
        photo.CollectionId = Guid.NewGuid(); photo.CollectionCoverValidatedAtUtc = DateTime.UtcNow.AddHours(-29);
        db.Photos.Add(photo); await db.SaveChangesAsync(ct);
        var repo = Repository(photo); var storage = new Mock<IStorageService>(); var references = new Mock<ICollectionCoverReferenceLookup>();
        references.Setup(r => r.IsCurrentAsync(photo.OwnerId, photo.CollectionId.Value, photo.Id, ct)).ReturnsAsync(current);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(ct)).Returns(() => db.SaveChangesAsync(ct));
        await new CollectionCoverCleanup(db, repo.Object, storage.Object, references.Object, TimeProvider.System, uow.Object).RunAsync(ct);
        if (current == true)
        {
            Assert.Equal(PhotoStatus.Confirmed, photo.Status);
            storage.Verify(s => s.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, ct), Times.Once);
        }
        else if (current == false)
        {
            Assert.Empty(db.Photos);
            storage.Verify(s => s.Delete(photo.StorageKey, BucketType.CheckIn, ct), Times.Once);
            storage.Verify(s => s.Delete(photo.OriginalStorageKey, BucketType.CheckIn, ct), Times.Once);
        }
        else
        {
            Assert.Equal(PhotoStatus.Pending, photo.Status);
            storage.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task RecentlyClaimedOldUploadIsRetainedWhileItsCollectionSaveMayStillBeInFlight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var photo = Photo("collection-covers/sealed/recent-claim.jpg", DateTime.UtcNow.AddHours(-25));
        photo.OriginalStorageKey = "collection-covers/recent-upload.jpg";
        photo.CollectionId = Guid.NewGuid();
        photo.ClaimedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        photo.CollectionCoverValidatedAtUtc = photo.ClaimedAtUtc;
        db.Photos.Add(photo); await db.SaveChangesAsync(ct);
        var repo = Repository(photo); var storage = new Mock<IStorageService>(MockBehavior.Strict);
        var references = new Mock<ICollectionCoverReferenceLookup>();
        references.Setup(r => r.IsCurrentAsync(photo.OwnerId, photo.CollectionId.Value, photo.Id, ct)).ReturnsAsync(false);
        var uow = new Mock<IUnitOfWork>();

        await new CollectionCoverCleanup(db, repo.Object, storage.Object, references.Object, TimeProvider.System, uow.Object).RunAsync(ct);

        Assert.Equal(PhotoStatus.Pending, photo.Status);
        Assert.Single(db.Photos);
        references.VerifyNoOtherCalls();
        storage.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClaimCompletedAfterCandidateSelectionIsRecheckedUnderThePhotoLock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var photo = Photo("collection-covers/late-claim.jpg", DateTime.UtcNow.AddHours(-25));
        photo.CollectionId = Guid.NewGuid();
        photo.ClaimedAtUtc = photo.UploadedAt;
        db.Photos.Add(photo); await db.SaveChangesAsync(ct);
        var repo = Repository(photo);
        repo.Setup(r => r.GetClaimPhotosAsync(photo.OwnerId, It.IsAny<IReadOnlyCollection<string>>(), ct))
            .Callback(() => { photo.ClaimedAtUtc = DateTime.UtcNow; photo.CollectionCoverValidatedAtUtc = DateTime.UtcNow; })
            .ReturnsAsync([photo]);
        var storage = new Mock<IStorageService>(MockBehavior.Strict);
        var references = new Mock<ICollectionCoverReferenceLookup>();
        references.Setup(r => r.IsCurrentAsync(photo.OwnerId, photo.CollectionId.Value, photo.Id, ct)).ReturnsAsync(false);
        var uow = new Mock<IUnitOfWork>();

        await new CollectionCoverCleanup(db, repo.Object, storage.Object, references.Object, TimeProvider.System, uow.Object).RunAsync(ct);

        Assert.Equal(PhotoStatus.Pending, photo.Status);
        Assert.Single(db.Photos);
        references.VerifyNoOtherCalls();
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AbandonedUploadExpiresWhileFreshUploadsAndOtherPurposesRemainUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var orphan = Photo("collection-covers/orphan.jpg", DateTime.UtcNow.AddHours(-30));
        var fresh = Photo("collection-covers/fresh.jpg", DateTime.UtcNow);
        var checkIn = Photo("check-ins/old.jpg", DateTime.UtcNow.AddHours(-30));
        db.Photos.AddRange(orphan, fresh, checkIn); await db.SaveChangesAsync(ct);
        var repo = Repository(orphan); var storage = new Mock<IStorageService>();
        var references = new Mock<ICollectionCoverReferenceLookup>(MockBehavior.Strict); var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(ct)).Returns(() => db.SaveChangesAsync(ct));
        await new CollectionCoverCleanup(db, repo.Object, storage.Object, references.Object, TimeProvider.System, uow.Object).RunAsync(ct);
        Assert.Equal(new[] { checkIn.Id, fresh.Id }.Order(), db.Photos.Select(p => p.Id).ToArray().Order());
        storage.Verify(s => s.Delete(orphan.StorageKey, BucketType.CheckIn, ct), Times.Once);
        storage.Verify(s => s.Delete($"collection-covers/sealed/{orphan.Id:N}.jpg", BucketType.CheckIn, ct), Times.Once);
        references.VerifyNoOtherCalls();
    }

    private static Mock<IPhotoRepository> Repository(PhotoMetadata photo)
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.AcquireClaimLocksAsync(photo.OwnerId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        repo.Setup(r => r.GetClaimPhotosAsync(photo.OwnerId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([photo]);
        return repo;
    }

    private static PhotoMetadata Photo(string key, DateTime uploaded) => new()
    {
        Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), StorageKey = key, FileName = "cover.jpg", ContentType = "image/jpeg",
        SizeBytes = 100, BucketType = BucketType.CheckIn, UploadedAt = uploaded, Status = PhotoStatus.Pending
    };
}
