using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class ShopPhotoClaimTests
{
    private readonly Mock<IPhotoRepository> _repository = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ShopPhotoClaimTests()
    {
        _repository.Setup(x => x.AcquireClaimLocksAsync(It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new NoopLease());
    }

    [Fact]
    public async Task Claim_ForeignOrMissingUpload_DoesNotReserve()
    {
        _repository.Setup(x => x.GetClaimPhotosAsync(_ownerId,
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([]);

        var result = await new ClaimShopPhotosHandler(_repository.Object, _storage.Object)
            .ClaimAsync(new ClaimShopPhotosRequest(_shopId, ShopPhotoAttachmentKind.Gallery,
                ["shops/foreign.jpg"]), _ownerId, _ct);

        Assert.False(result.IsSuccess);
        _repository.Verify(x => x.TryClaimShopBatchAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(),
            It.IsAny<Guid>(), It.IsAny<ShopPhotoAttachmentKind>(), _ct), Times.Never);
    }

    [Fact]
    public async Task Claim_LostReplyRetryOnSameDestination_ReturnsClaimedMetadata()
    {
        var photo = Photo();
        photo.ShopAttachmentId = _shopId;
        photo.ShopAttachmentKind = ShopPhotoAttachmentKind.Menu;
        photo.Status = PhotoStatus.Confirmed;
        _repository.Setup(x => x.GetClaimPhotosAsync(_ownerId,
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([photo]);
        _repository.Setup(x => x.TryClaimShopBatchAsync(It.Is<IReadOnlyList<Guid>>(ids =>
                    ids.Count == 1 && ids[0] == photo.Id), _ownerId, _shopId,
                ShopPhotoAttachmentKind.Menu, _ct)).ReturnsAsync(true);
        _storage.Setup(x => x.GetObjectSize(photo.StorageKey, BucketType.Shop, _ct))
            .ReturnsAsync(128L);

        var result = await new ClaimShopPhotosHandler(_repository.Object, _storage.Object)
            .ClaimAsync(new ClaimShopPhotosRequest(_shopId, ShopPhotoAttachmentKind.Menu,
                [photo.StorageKey]), _ownerId, _ct);

        Assert.True(result.IsSuccess);
        var claimed = Assert.Single(result.Data);
        Assert.Equal(photo.Id, claimed.PhotoId);
        Assert.Equal(128L, claimed.SizeBytes);
    }

    [Fact]
    public async Task Claim_DifferentShopCannotTakeExistingReservation()
    {
        var photo = Photo();
        photo.ShopAttachmentId = Guid.NewGuid();
        photo.ShopAttachmentKind = ShopPhotoAttachmentKind.Gallery;
        _repository.Setup(x => x.GetClaimPhotosAsync(_ownerId,
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([photo]);

        var result = await new ClaimShopPhotosHandler(_repository.Object, _storage.Object)
            .ClaimAsync(new ClaimShopPhotosRequest(_shopId, ShopPhotoAttachmentKind.Gallery,
                [photo.StorageKey]), _ownerId, _ct);

        Assert.False(result.IsSuccess);
        _repository.Verify(x => x.TryClaimShopBatchAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(),
            It.IsAny<Guid>(), It.IsAny<ShopPhotoAttachmentKind>(), _ct), Times.Never);
    }

    [Fact]
    public async Task AttachedEvent_AfterOperatorRelease_CannotResurrectPhoto()
    {
        var photo = Photo();
        _repository.Setup(x => x.GetByStorageKeysAsync(_ownerId,
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([photo]);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ShopPhotosAttachedHandler(_repository.Object, _storage.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ShopPhotosAttachedEvent(_ownerId, _shopId,
                ShopPhotoAttachmentKind.Gallery, [photo.StorageKey]), _ct));

        Assert.Equal(PhotoStatus.Pending, photo.Status);
        _storage.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AttachedEvent_FinalizesMatchingClaimAndIsIdempotent()
    {
        var photo = Photo();
        photo.ShopAttachmentId = _shopId;
        photo.ShopAttachmentKind = ShopPhotoAttachmentKind.Gallery;
        _repository.Setup(x => x.GetByStorageKeysAsync(_ownerId,
                It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([photo]);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ShopPhotosAttachedHandler(_repository.Object, _storage.Object, unitOfWork.Object);
        var message = new ShopPhotosAttachedEvent(_ownerId, _shopId,
            ShopPhotoAttachmentKind.Gallery, [photo.StorageKey]);

        await handler.Handle(message, _ct);
        await handler.Handle(message, _ct);

        Assert.Equal(PhotoStatus.Confirmed, photo.Status);
        _storage.Verify(x => x.MarkAsPermanent(photo.StorageKey, BucketType.Shop, _ct), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
    }

    private PhotoMetadata Photo() => new()
    {
        Id = Guid.NewGuid(), OwnerId = _ownerId, StorageKey = "shops/a.jpg",
        FileName = "original.jpg", ContentType = "image/jpeg", SizeBytes = 10,
        UploadedAt = DateTime.UtcNow, Status = PhotoStatus.Pending,
        BucketType = BucketType.Shop
    };

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
