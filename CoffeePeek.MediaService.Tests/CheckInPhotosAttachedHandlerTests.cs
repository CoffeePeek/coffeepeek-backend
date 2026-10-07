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

    [Fact]
    public async Task CanonicalAttachments_UseOwnerScopedIdempotentConfirmation()
    {
        var own = Photo("check-ins/new");
        var foreign = Photo("check-ins/foreign", Guid.NewGuid());
        SetupRepo(own, foreign);
        var message = new CheckInPhotosAttachedEvent(_userId, Guid.NewGuid(), [own.StorageKey, foreign.StorageKey], DateTime.UtcNow);
        own.BucketType = BucketType.CheckIn; own.CheckInId = message.CheckInId;
        await CreateHandler().Handle(message, TestContext.Current.CancellationToken);
        await CreateHandler().Handle(message, TestContext.Current.CancellationToken);
        own.Status.Should().Be(PhotoStatus.Confirmed);
        foreign.Status.Should().Be(PhotoStatus.Pending);
        _storageMock.Verify(s => s.MarkAsPermanent("check-ins/new", BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
        _storageMock.Verify(s => s.MarkAsPermanent("check-ins/foreign", It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CanonicalAttachments_StorageFailureRetainsPendingAndCanRetry()
    {
        var own = Photo("check-ins/new");
        SetupRepo(own);
        var message = new CheckInPhotosAttachedEvent(_userId, Guid.NewGuid(), [own.StorageKey], DateTime.UtcNow);
        own.BucketType = BucketType.CheckIn; own.CheckInId = message.CheckInId;
        _storageMock.SetupSequence(s => s.MarkAsPermanent(own.StorageKey, BucketType.CheckIn, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary")) .Returns(Task.CompletedTask);
        var act = () => CreateHandler().Handle(message, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
        own.Status.Should().Be(PhotoStatus.Pending);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await act();
        own.Status.Should().Be(PhotoStatus.Confirmed);
    }

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
        var foreign = Photo("check-ins/foreign", ownerId: Guid.NewGuid());
        var alreadyConfirmed = Photo("reviews/done", status: PhotoStatus.Confirmed);
        SetupRepo(own, foreign, alreadyConfirmed);

        await CreateHandler().Handle(Event("reviews/own", "check-ins/foreign", "reviews/done"), CancellationToken.None);

        own.Status.Should().Be(PhotoStatus.Confirmed);
        foreign.Status.Should().Be(PhotoStatus.Pending);
        alreadyConfirmed.Status.Should().Be(PhotoStatus.Confirmed);
        _storageMock.Verify(s => s.MarkAsPermanent("reviews/own", BucketType.Shop, It.IsAny<CancellationToken>()), Times.Once);
        _storageMock.Verify(s => s.MarkAsPermanent("check-ins/foreign", It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task Handle_ReleasedPrivateClaim_IsNotRestoredByAStaleEvent()
    {
        var photo = Photo("check-ins/private"); photo.BucketType = BucketType.CheckIn;
        SetupRepo(photo);
        await CreateHandler().Handle(Event(photo.StorageKey), TestContext.Current.CancellationToken);
        photo.CheckInId.Should().BeNull(); photo.Status.Should().Be(PhotoStatus.Pending);
        _storageMock.VerifyNoOtherCalls(); _uowMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_CurrentPrivateClaim_ConfirmsOnlyItsCanonicalParent()
    {
        var message = Event("check-ins/private");
        var photo = Photo("check-ins/private"); photo.BucketType = BucketType.CheckIn; photo.CheckInId = message.CheckInId;
        SetupRepo(photo);
        await CreateHandler().Handle(message, TestContext.Current.CancellationToken);
        photo.CheckInId.Should().Be(message.CheckInId); photo.Status.Should().Be(PhotoStatus.Confirmed);
        _storageMock.Verify(s => s.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
    }
}
