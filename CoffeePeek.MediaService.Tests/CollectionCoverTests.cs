using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SkiaSharp;
using Wolverine;

namespace CoffeePeek.MediaService.Tests;

public class CollectionCoverTests
{
    private const string Key = "collection-covers/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg";
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _bag = Guid.NewGuid();
    private readonly Mock<IPhotoRepository> _repo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private PhotoMetadata Upload(string key = Key) => new()
    {
        Id = Guid.NewGuid(), OwnerId = _owner, FileName = "bag.jpg", ContentType = "image/jpeg", StorageKey = key,
        SizeBytes = 98304, UploadedAt = DateTime.UtcNow, BucketType = BucketType.CheckIn, Status = PhotoStatus.Pending
    };

    private static byte[] Image(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(120, 60, 20));
        using var data = bitmap.Encode(format, 80);
        return data.ToArray();
    }

    private void Stored(PhotoMetadata photo, byte[] bytes)
    {
        _repo.Setup(r => r.AcquireClaimLocksAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _repo.Setup(r => r.GetClaimPhotosAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([photo]);
        _repo.Setup(r => r.TryClaimCollectionCoverAsync(photo.Id, _owner, _bag, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sealedKey = $"collection-covers/sealed/{photo.Id:N}.jpg";
        var sealedExists = false;
        _storage.Setup(s => s.GetObjectSize(It.IsAny<string>(), BucketType.CheckIn, It.IsAny<CancellationToken>())).ReturnsAsync(bytes.Length);
        _storage.Setup(s => s.GetObjectSize(sealedKey, BucketType.CheckIn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sealedExists ? bytes.Length : (long?)null);
        _storage.Setup(s => s.SealCollectionCoverObjectAsync(It.IsAny<string>(), photo.Id, It.IsAny<CancellationToken>()))
            .Callback(() => sealedExists = true).ReturnsAsync(sealedKey);
        _storage.Setup(s => s.CopyToAsync(sealedKey, BucketType.CheckIn, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string _, BucketType _, Stream target, CancellationToken ct) => target.WriteAsync(bytes, ct).AsTask());
    }

    private Task<Response<ClaimedCollectionCoverDto>> Claim(string key = Key, Guid? bag = null) =>
        new ClaimCollectionCoverHandler(_repo.Object, _storage.Object, _uow.Object)
            .ClaimAsync(new ClaimCollectionCoverRequest(bag ?? _bag, key), _owner, Ct);

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task ValidCoverIsSealedPrivatelyButRemainsTemporaryUntilDiscoveryCommits(SKEncodedImageFormat format, string contentType)
    {
        var photo = Upload();
        Stored(photo, Image(format, 720, 960));

        var result = await Claim();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(photo.Id, result.Data.PhotoId);
        Assert.Equal($"collection-covers/sealed/{photo.Id:N}.jpg", result.Data.StorageKey);
        Assert.Equal(contentType, result.Data.ContentType);
        Assert.Equal(PhotoStatus.Pending, photo.Status);
        Assert.NotNull(photo.CollectionCoverValidatedAtUtc);
        _storage.Verify(s => s.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(_bag, photo.CollectionId);
        Assert.Equal(Key, photo.OriginalStorageKey);
        _storage.Verify(s => s.SealCollectionCoverObjectAsync(Key, photo.Id, It.IsAny<CancellationToken>()), Times.Once);

        // Retrying the same collection is idempotent and does not copy again.
        var retry = await Claim();
        Assert.True(retry.IsSuccess);
        _storage.Verify(s => s.SealCollectionCoverObjectAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACardAttachedToAnotherBagIsAConflict()
    {
        var photo = Upload();
        Stored(photo, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        photo.CollectionId = Guid.NewGuid();
        photo.Status = PhotoStatus.Confirmed;

        var result = await Claim();

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Theory]
    [InlineData("oversized-side")]
    [InlineData("gif")]
    [InlineData("not-an-image")]
    public async Task InvalidBytesAreRejectedAndTheSealedCopyIsRemoved(string kind)
    {
        var photo = Upload();
        var bytes = kind switch
        {
            "oversized-side" => Image(SKEncodedImageFormat.Jpeg, 1281, 10),
            "gif" => "GIF89a-not-a-card"u8.ToArray(),
            _ => "plain text pretending"u8.ToArray()
        };
        Stored(photo, bytes);

        var result = await Claim();

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(PhotoStatus.Deleted, photo.Status);
        _storage.Verify(s => s.Delete($"collection-covers/sealed/{photo.Id:N}.jpg", BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadsLargerThanOneMebibyteAreRejectedBeforeCopying()
    {
        var photo = Upload();
        Stored(photo, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        _storage.Setup(s => s.GetObjectSize(Key, BucketType.CheckIn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateCardPhotoValidation.MaxSizeBytes + 1L);

        var result = await Claim();

        Assert.Equal(400, result.StatusCode);
        _repo.Verify(r => r.TryClaimCollectionCoverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OtherUploadsAndPurposesAreRejected()
    {
        var checkIn = Upload("check-ins/1.jpg");
        Stored(checkIn, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        Assert.Equal(400, (await Claim("check-ins/1.jpg")).StatusCode);

        var claimedByCheckIn = Upload();
        claimedByCheckIn.CheckInId = Guid.NewGuid();
        Stored(claimedByCheckIn, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        Assert.Equal(400, (await Claim()).StatusCode);
        _storage.Verify(s => s.SealCollectionCoverObjectAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckInClaimsCannotTakeAShelfUpload()
    {
        var shelf = Upload();
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.AcquireClaimLocksAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        repo.Setup(r => r.GetClaimPhotosAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([shelf]);
        var result = await new ClaimCheckInPhotosHandler(repo.Object, _storage.Object, _uow.Object)
            .ClaimAsync(new CoffeePeek.Contract.Dtos.CheckIns.ClaimCheckInPhotosRequest(Guid.NewGuid(), [Key]), _owner, Ct);
        Assert.False(result.IsSuccess);
        repo.Verify(r => r.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OwnerType.OwnershipEvidence, false)]
    [InlineData(OwnerType.User, true)]
    public async Task OwnershipEvidenceCannotBecomeAShelfCard(OwnerType ownerType, bool attached)
    {
        var photo = Upload();
        photo.OwnerType = ownerType;
        if (attached) photo.OwnershipClaimId = Guid.NewGuid();
        Stored(photo, Image(SKEncodedImageFormat.Jpeg, 10, 10));

        Assert.Equal(400, (await Claim()).StatusCode);

        _repo.Verify(r => r.TryClaimCollectionCoverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _storage.Verify(s => s.SealCollectionCoverObjectAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleasedCardIsDeletedOnceAndOnlyForItsCollection()
    {
        var photo = Upload($"collection-covers/{Guid.NewGuid():N}.jpg");
        photo.CollectionId = _bag;
        photo.Status = PhotoStatus.Confirmed;
        Stored(photo, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        _repo.Setup(r => r.GetByIdAsync(photo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(photo);
        var handler = new CollectionCoverReleasedHandler(_repo.Object, _storage.Object, _uow.Object);

        await handler.Handle(new CollectionCoverReleasedEvent(_owner, Guid.NewGuid(), photo.Id, DateTime.UtcNow), Ct);
        Assert.Equal(PhotoStatus.Confirmed, photo.Status);

        await handler.Handle(new CollectionCoverReleasedEvent(_owner, _bag, photo.Id, DateTime.UtcNow), Ct);
        await handler.Handle(new CollectionCoverReleasedEvent(_owner, _bag, photo.Id, DateTime.UtcNow), Ct);
        Assert.Equal(PhotoStatus.Deleted, photo.Status);
        _storage.Verify(s => s.Delete(photo.StorageKey, BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnlyCommittedAttachmentMakesValidatedCoverPermanentAndDelayedAttachCannotResurrectReleasedBytes()
    {
        var photo = Upload();
        Stored(photo, Image(SKEncodedImageFormat.Jpeg, 10, 10));
        _repo.Setup(r => r.GetByIdAsync(photo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(photo);
        Assert.True((await Claim()).IsSuccess);
        var handler = new CollectionCoverAttachedHandler(_repo.Object, _storage.Object, _uow.Object);
        await handler.Handle(new CollectionCoverAttachedEvent(Guid.NewGuid(), _bag, photo.Id), Ct);
        Assert.Equal(PhotoStatus.Pending, photo.Status);
        await handler.Handle(new CollectionCoverAttachedEvent(_owner, _bag, photo.Id), Ct);
        await handler.Handle(new CollectionCoverAttachedEvent(_owner, _bag, photo.Id), Ct);
        Assert.Equal(PhotoStatus.Confirmed, photo.Status);
        _storage.Verify(s => s.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
        await new CollectionCoverReleasedHandler(_repo.Object, _storage.Object, _uow.Object)
            .Handle(new CollectionCoverReleasedEvent(_owner, _bag, photo.Id, DateTime.UtcNow), Ct);
        await handler.Handle(new CollectionCoverAttachedEvent(_owner, _bag, photo.Id), Ct);
        Assert.Equal(PhotoStatus.Deleted, photo.Status);
    }

    [Fact]
    public async Task UploadRouteIssuesOnlyOwnerBoundPrivateCollectionKeys()
    {
        var bus = new Mock<IMessageBus>(); var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(_owner);
        GenerateShopPhotosCommand? captured = null;
        bus.Setup(b => b.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = (GenerateShopPhotosCommand)m)
            .ReturnsAsync(Response<List<GenerateUploadUrlResponse>>.Success([]));
        var controller = new PhotosController(bus.Object, user.Object);
        var request = new PhotoRequest(100, "cover.jpg", "image/jpeg");
        Assert.IsType<OkObjectResult>(await controller.GenerateCollectionCoverUploadUrl([request], Ct));
        Assert.Equal("collection-covers", captured!.StorageKeyPrefix);
        Assert.Equal(BucketType.CheckIn, captured.Bucket);
        Assert.Equal(_owner, captured.OwnerId);
        Assert.IsType<BadRequestObjectResult>(await controller.GenerateCollectionCoverUploadUrl([request with { ContentType = "image/gif" }], Ct));
        Assert.IsType<BadRequestObjectResult>(await controller.GenerateCollectionCoverUploadUrl([request with { SizeBytes = 1048577 }], Ct));
        Assert.IsType<BadRequestObjectResult>(await controller.GenerateCollectionCoverUploadUrl([request, request], Ct));
    }
}
