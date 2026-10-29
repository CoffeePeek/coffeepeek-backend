using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using Moq;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;

public class ShelfBagPhotoRecoveryTests
{
    [Theory]
    [InlineData("copy")]
    [InlineData("delete")]
    [InlineData("save")]
    public async Task RetryRecoversSealedCardAfterInterruptedClaim(string failureStage)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var bag = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var uploadKey = $"shelf-bags/{Guid.NewGuid()}.png";
        var sealedKey = $"shelf-bags/{photoId:N}.png";
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Brown);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = image.ToArray();

        PhotoMetadata Metadata() => new()
        {
            Id = photoId, OwnerId = owner, FileName = "bag.png", ContentType = "image/png", StorageKey = uploadKey,
            SizeBytes = bytes.Length, UploadedAt = DateTime.UtcNow, BucketType = BucketType.CheckIn, Status = PhotoStatus.Pending
        };
        var tracked = Metadata();
        var sealedExists = false;
        var uploadExists = true;
        var interrupted = false;
        var copies = 0;
        var repo = new Mock<IPhotoRepository>();
        var storage = new Mock<IStorageService>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.AcquireClaimLocksAsync(owner, It.IsAny<IReadOnlyCollection<string>>(), ct))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        repo.Setup(r => r.GetClaimPhotosAsync(owner, It.IsAny<IReadOnlyCollection<string>>(), ct))
            .ReturnsAsync(() => new List<PhotoMetadata> { tracked });
        repo.Setup(r => r.TryClaimShelfBagAsync(photoId, owner, bag, ct)).ReturnsAsync(true);
        storage.Setup(s => s.GetObjectSize(It.IsAny<string>(), BucketType.CheckIn, ct))
            .Returns((string key, BucketType _, CancellationToken _) => Task.FromResult<long?>(
                key == sealedKey ? (sealedExists ? bytes.Length : null)
                : uploadExists ? (interrupted ? ShelfBagPhotoValidation.MaxSizeBytes + 1 : bytes.Length) : null));
        storage.Setup(s => s.SealShelfBagObjectAsync(uploadKey, photoId, ct))
            .Returns(() =>
            {
                // Model the storage contract: a retry keeps the first immutable copy.
                if (!sealedExists) copies++;
                sealedExists = true;
                if (!interrupted && failureStage == "copy")
                {
                    interrupted = true;
                    throw new IOException("Interrupted after copying, before removing the upload.");
                }
                uploadExists = false;
                if (!interrupted && failureStage == "delete")
                {
                    interrupted = true;
                    throw new IOException("Interrupted after removing the upload.");
                }
                return Task.FromResult(sealedKey);
            });
        storage.Setup(s => s.CopyToAsync(sealedKey, BucketType.CheckIn, It.IsAny<Stream>(), ct))
            .Returns((string _, BucketType _, Stream target, CancellationToken token) => target.WriteAsync(bytes, token).AsTask());
        uow.Setup(u => u.SaveChangesAsync(ct)).Returns(() =>
        {
            if (!interrupted && failureStage == "save")
            {
                interrupted = true;
                throw new IOException("Metadata commit failed after sealing.");
            }
            return Task.FromResult(1);
        });
        var handler = new ClaimShelfBagPhotoHandler(repo.Object, storage.Object, uow.Object);
        var request = new ClaimShelfBagPhotoRequest(bag, uploadKey);
        await Assert.ThrowsAsync<IOException>(() => handler.ClaimAsync(request, owner, ct));
        Assert.True(sealedExists);

        // A new request reloads the old key from the DB; only TryClaimShelfBag's reservation was committed.
        tracked = Metadata();
        tracked.ShelfBagId = bag;
        tracked.ClaimedAtUtc = DateTime.UtcNow;
        var retry = await handler.ClaimAsync(request, owner, ct);

        Assert.True(retry.IsSuccess, retry.Message);
        Assert.Equal(photoId, retry.Data.PhotoId);
        Assert.Equal(sealedKey, retry.Data.StorageKey);
        Assert.Equal(uploadKey, tracked.OriginalStorageKey);
        Assert.Equal(PhotoStatus.Confirmed, tracked.Status);
        Assert.Equal(bag, tracked.ShelfBagId);
        Assert.Equal(1, copies);
        storage.Verify(s => s.CopyToAsync(sealedKey, BucketType.CheckIn, It.IsAny<Stream>(), ct), Times.Once);
    }
}
