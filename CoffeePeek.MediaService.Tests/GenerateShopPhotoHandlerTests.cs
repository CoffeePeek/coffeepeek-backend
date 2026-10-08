using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using FluentAssertions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class GenerateShopPhotoHandlerTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/octet-stream")]
    public async Task UnsafeMimeInBatch_DoesNotIssueAnyUploadUrlOrWriteMetadata(string contentType)
    {
        var storage = new Mock<IStorageService>(); var repository = new Mock<IPhotoRepository>();
        var command = new GenerateShopPhotosCommand([
            new PhotoRequest(42, "safe.jpg", "image/jpeg"), new PhotoRequest(42, "unsafe.jpg", contentType)
        ], Guid.NewGuid(), "check-ins", BucketType.CheckIn);
        var (response, _) = await GenerateShopPhotoHandler.Handle(command, storage.Object, repository.Object,
            TestContext.Current.CancellationToken);
        response.IsSuccess.Should().BeFalse();
        storage.VerifyNoOtherCalls(); repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/bmp")]
    [InlineData("image/avif")]
    [InlineData("IMAGE/JPEG")]
    public async Task SafeRasterMime_IssuesUploadAndStoresCanonicalMime(string contentType)
    {
        var storage = new Mock<IStorageService>(); var repository = new Mock<IPhotoRepository>();
        storage.Setup(s => s.GetPresignedUploadUrl("safe.jpg", contentType.ToLowerInvariant(), BucketType.CheckIn,
                It.IsAny<CancellationToken>(), "check-ins"))
            .ReturnsAsync(new PresignedPhotoMetaData("http://upload", "upload-key"));
        var command = new GenerateShopPhotosCommand([new PhotoRequest(42, "safe.jpg", contentType)],
            Guid.NewGuid(), "check-ins", BucketType.CheckIn);
        var (response, _) = await GenerateShopPhotoHandler.Handle(command, storage.Object, repository.Object,
            TestContext.Current.CancellationToken);
        response.IsSuccess.Should().BeTrue();
        repository.Verify(r => r.AddRange(It.Is<IEnumerable<PhotoMetadata>>(photos =>
            photos.Single().ContentType == contentType.ToLowerInvariant()
            && photos.Single().BucketType == BucketType.CheckIn
            && photos.Single().OwnerId == command.OwnerId)), Times.Once);
    }
}
