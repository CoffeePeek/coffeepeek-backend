using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Kernel;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class ShelfBagCardCopyTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _bag = Guid.NewGuid();
    private readonly Guid _checkIn = Guid.NewGuid();
    private readonly Mock<IPhotoRepository> _repo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private PhotoMetadata Card() => new()
    {
        Id = Guid.NewGuid(), OwnerId = _owner, FileName = "bag.jpg", ContentType = "image/jpeg", OwnerType = OwnerType.User,
        StorageKey = "shelf-bags/card.jpg", SizeBytes = 2048, UploadedAt = DateTime.UtcNow, BucketType = BucketType.CheckIn,
        ShelfBagId = _bag, Status = PhotoStatus.Confirmed
    };

    private CopyShelfBagCardHandler Handler() => new(_repo.Object, _storage.Object, _uow.Object);

    [Fact]
    public async Task CopiesTheCardIntoAPendingPhotoReservedForTheCheckIn()
    {
        var card = Card();
        var copyId = Guid.NewGuid();
        var key = $"check-ins/{copyId:N}.jpg";
        _repo.Setup(r => r.GetShelfBagPhotoAsync(_bag, card.Id, _owner, Ct)).ReturnsAsync(card);
        _storage.SetupSequence(s => s.GetObjectSize(key, BucketType.CheckIn, Ct)).ReturnsAsync((long?)null).ReturnsAsync(2048);
        PhotoMetadata? added = null;
        _repo.Setup(r => r.Add(It.IsAny<PhotoMetadata>())).Callback<PhotoMetadata>(p => added = p);

        var result = await Handler().CopyAsync(new CopyShelfBagCardRequest(_checkIn, _bag, card.Id, copyId), _owner, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(copyId, result.Data!.PhotoId);
        Assert.Equal(key, result.Data.StorageKey);
        _storage.Verify(s => s.CopyToAsync(card.StorageKey, BucketType.CheckIn, It.IsAny<Stream>(), Ct), Times.Once);
        _storage.Verify(s => s.Put(key, "image/jpeg", It.IsAny<Stream>(), It.IsAny<long>(), BucketType.CheckIn, Ct), Times.Once);
        Assert.NotNull(added);
        Assert.Equal(PhotoStatus.Pending, added!.Status);
        Assert.Equal(_checkIn, added.CheckInId);
        Assert.Null(added.ShelfBagId);
    }

    [Fact]
    public async Task RetryReturnsTheSameCopyAndAnotherOwnersCardIsNotFound()
    {
        var card = Card();
        var copyId = Guid.NewGuid();
        var existing = new PhotoMetadata
        {
            Id = copyId, OwnerId = _owner, CheckInId = _checkIn, Status = PhotoStatus.Pending, BucketType = BucketType.CheckIn,
            StorageKey = $"check-ins/{copyId:N}.jpg", FileName = "shelf-bag.jpg", ContentType = "image/jpeg", SizeBytes = 2048
        };
        _repo.Setup(r => r.GetShelfBagPhotoAsync(_bag, card.Id, _owner, Ct)).ReturnsAsync(card);
        _repo.Setup(r => r.GetByIdAsync(copyId, Ct)).ReturnsAsync(existing);

        var retried = await Handler().CopyAsync(new CopyShelfBagCardRequest(_checkIn, _bag, card.Id, copyId), _owner, Ct);
        var stranger = await Handler().CopyAsync(new CopyShelfBagCardRequest(_checkIn, _bag, card.Id, Guid.NewGuid()),
            Guid.NewGuid(), Ct);

        Assert.Equal(copyId, retried.Data!.PhotoId);
        Assert.False(stranger.IsSuccess);
        _storage.Verify(s => s.Put(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(),
            It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetachedCopyLosesItsBytesOnceAndOtherPhotosAreLeftAlone()
    {
        var copy = new PhotoMetadata
        {
            Id = Guid.NewGuid(), OwnerId = _owner, CheckInId = _checkIn, Status = PhotoStatus.Confirmed,
            BucketType = BucketType.CheckIn, StorageKey = "check-ins/copy.jpg", FileName = "shelf-bag.jpg", ContentType = "image/jpeg"
        };
        _repo.Setup(r => r.GetByIdAsync(copy.Id, Ct)).ReturnsAsync(copy);
        var variants = new Mock<IPhotoVariantQueue>();
        var handler = new CheckInPhotoDetachedHandler(_repo.Object, _storage.Object, variants.Object, _uow.Object);

        await handler.Handle(new CheckInPhotoDetachedEvent(_owner, Guid.NewGuid(), copy.Id, DateTime.UtcNow), Ct);
        Assert.Equal(PhotoStatus.Confirmed, copy.Status);

        await handler.Handle(new CheckInPhotoDetachedEvent(_owner, _checkIn, copy.Id, DateTime.UtcNow), Ct);
        await handler.Handle(new CheckInPhotoDetachedEvent(_owner, _checkIn, copy.Id, DateTime.UtcNow), Ct);

        Assert.Equal(PhotoStatus.Deleted, copy.Status);
        _storage.Verify(s => s.Delete("check-ins/copy.jpg", BucketType.CheckIn, Ct), Times.Once);
        variants.Verify(v => v.CancelPhotoAsync(copy.Id, Ct), Times.Once);
    }
}
