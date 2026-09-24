using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInPhotosAttachedHandlerTests
{
    private readonly Mock<IPhotoRepository> _repoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IStorageService> _storageMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private PhotoMetadata Photo(string key, Guid? ownerId = null, PhotoStatus status = PhotoStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "a.jpg",
        ContentType = "image/jpeg",
        StorageKey = key,
        SizeBytes = 10,
        UploadedAt = DateTime.UtcNow,
        Status = status,
        BucketType = BucketType.Shop,
        OwnerType = OwnerType.Shop,
        OwnerId = ownerId ?? _userId
    };

    private CheckInPhotosAttachedHandler CreateHandler() =>
        new(_repoMock.Object, _uowMock.Object, _storageMock.Object);

    private CheckInPhotosAttachedEvent Event(params string[] keys) =>
        new(_userId, Guid.NewGuid(), keys, DateTime.UtcNow);

    private void SetupRepo(params PhotoMetadata[] photos) =>
        _repoMock.Setup(r => r.GetByStorageKeysAsync(
                _userId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => photos.ToList());

    [Fact]
    public async Task Handle_MarksOwnPendingPhotosPermanentAndConfirmed_SkipsOthers_SavesOnce()
    {
        var own = Photo("reviews/own");
        var foreign = Photo("reviews/foreign", ownerId: Guid.NewGuid());
        var alreadyConfirmed = Photo("reviews/done", status: PhotoStatus.Confirmed);
        SetupRepo(own, foreign, alreadyConfirmed);

        await CreateHandler().Handle(Event("reviews/own", "reviews/foreign", "reviews/done"), CancellationToken.None);

        own.Status.Should().Be(PhotoStatus.Confirmed);
        foreign.Status.Should().Be(PhotoStatus.Pending);
        alreadyConfirmed.Status.Should().Be(PhotoStatus.Confirmed);
        _storageMock.Verify(s => s.MarkAsPermanent("reviews/own", BucketType.Shop, It.IsAny<CancellationToken>()), Times.Once);
        _storageMock.Verify(s => s.MarkAsPermanent("reviews/foreign", It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
        _storageMock.Verify(s => s.MarkAsPermanent("reviews/done", It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SecondDelivery_IsNoOp()
    {
        var own = Photo("reviews/own");
        SetupRepo(own);
        var message = Event("reviews/own");

        await CreateHandler().Handle(message, CancellationToken.None);
        await CreateHandler().Handle(message, CancellationToken.None);

        _storageMock.Verify(
            s => s.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StorageFailure_LeavesPhotoPendingAndDoesNotSave()
    {
        var own = Photo("reviews/own");
        SetupRepo(own);
        _storageMock.Setup(s => s.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var act = () => CreateHandler().Handle(Event("reviews/own"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        own.Status.Should().Be(PhotoStatus.Pending);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
