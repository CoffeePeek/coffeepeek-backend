using CoffeePeek.Contract.Dtos;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class ResolveUploadedPhotosHandlerTests
{
    private readonly Mock<IPhotoRepository> _repoMock = new();
    private readonly Mock<IStorageService> _storageMock = new();
    private readonly Guid _ownerId = Guid.NewGuid();

    private PhotoMetadata Photo(
        string key,
        Guid? ownerId = null,
        PhotoStatus status = PhotoStatus.Pending,
        DateTime? uploadedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "meta-name.jpg",
        ContentType = "image/jpeg",
        StorageKey = key,
        SizeBytes = 1,
        UploadedAt = uploadedAt ?? DateTime.UtcNow.AddMinutes(-5),
        Status = status,
        BucketType = BucketType.Shop,
        OwnerType = OwnerType.Shop,
        OwnerId = ownerId ?? _ownerId
    };

    private void SetupRepo(params PhotoMetadata[] photos) =>
        _repoMock.Setup(r => r.GetByStorageKeysAsync(
                _ownerId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos.ToList());

    private void SetupAnySize(long size) =>
        _storageMock.Setup(s => s.GetObjectSize(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(size);

    private Task<Response<List<ResolvedUploadedPhotoDto>>> Run(params string[] keys) =>
        ResolveUploadedPhotosHandler.Handle(
            new ResolveUploadedPhotosQuery(keys) { OwnerId = _ownerId },
            _repoMock.Object,
            _storageMock.Object,
            CancellationToken.None);

    [Fact]
    public async Task Handle_OwnPendingUploadedPhoto_ReturnsMetadataWithRealStorageSize()
    {
        SetupRepo(Photo("reviews/a"));
        _storageMock.Setup(s => s.GetObjectSize("reviews/a", BucketType.Shop, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4096L);

        var result = await Run("reviews/a");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().ContainSingle();
        var dto = result.Data[0];
        dto.StorageKey.Should().Be("reviews/a");
        dto.FileName.Should().Be("meta-name.jpg");
        dto.ContentType.Should().Be("image/jpeg");
        dto.SizeBytes.Should().Be(4096L);
    }

    [Fact]
    public async Task Handle_ForeignOwnerPhoto_IsOmitted()
    {
        SetupRepo(Photo("reviews/foreign", ownerId: Guid.NewGuid()));
        SetupAnySize(10);

        var result = await Run("reviews/foreign");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConfirmedPhoto_IsOmitted()
    {
        SetupRepo(Photo("reviews/used", status: PhotoStatus.Confirmed));
        SetupAnySize(10);

        var result = await Run("reviews/used");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CheckInBucketPhoto_IsOmittedFromShopUploadResolution()
    {
        var photo = Photo("check-ins/private");
        photo.BucketType = BucketType.CheckIn;
        SetupRepo(photo);
        SetupAnySize(10);

        var result = await Run(photo.StorageKey);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
        _storageMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_NonImageContentType_IsOmitted()
    {
        var photo = Photo("shop/file");
        photo.ContentType = "application/pdf";
        SetupRepo(photo);
        SetupAnySize(10);

        var result = await Run(photo.StorageKey);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
        _storageMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ExpiredPhoto_IsOmitted()
    {
        SetupRepo(Photo("reviews/old", uploadedAt: DateTime.UtcNow.AddHours(-25)));
        SetupAnySize(10);

        var result = await Run("reviews/old");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NotUploadedToStorage_IsOmitted()
    {
        SetupRepo(Photo("reviews/missing"));
        _storageMock.Setup(s => s.GetObjectSize("reviews/missing", BucketType.Shop, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var result = await Run("reviews/missing");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyKeyList_ReturnsError()
    {
        var result = await Run();

        result.IsSuccess.Should().BeFalse();
        _repoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_MoreThanTwentyKeys_ReturnsError()
    {
        var keys = Enumerable.Range(0, ResolveUploadedPhotosHandler.MaxStorageKeysPerRequest + 1)
            .Select(i => $"reviews/{i}")
            .ToArray();

        var result = await Run(keys);

        result.IsSuccess.Should().BeFalse();
        _repoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_DoesNotMutatePhotoOrStorage()
    {
        var photo = Photo("reviews/a");
        SetupRepo(photo);
        SetupAnySize(10);

        await Run("reviews/a");

        photo.Status.Should().Be(PhotoStatus.Pending);
        _storageMock.Verify(
            s => s.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
