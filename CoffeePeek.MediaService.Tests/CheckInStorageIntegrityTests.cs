using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using FluentAssertions;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInStorageIntegrityTests
{
    [Fact]
    public async Task ExistingPrivateDestination_IsReusedEvenWhenTheUploadStillExistsAndChanged()
    {
        var client = new Mock<IMinioClient>();
        client.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var targetBytes = "already verified"; var changedSource = "different upload"; var sourceExists = true;
        var targetStat = ObjectStat.FromResponseHeaders("target", new Dictionary<string,string> {
            ["Content-Length"]="16", ["Last-Modified"]=DateTimeOffset.UtcNow.ToString("r"), ["ETag"]="\"fixed\""
        });
        var statCalls = 0;
        client.Setup(c => c.StatObjectAsync(It.IsAny<StatObjectArgs>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (++statCalls == 1) return Task.FromResult(targetStat);
            sourceExists.Should().BeFalse();
            return Task.FromException<ObjectStat>(new ObjectNotFoundException("removed upload"));
        });
        client.Setup(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback(() => targetBytes = changedSource).Returns(Task.CompletedTask);
        client.Setup(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback(() => sourceExists = false).Returns(Task.CompletedTask);
        using var storage = new MinIOStorageService(new MinIOOptions(), client.Object);
        var photoId = Guid.NewGuid();
        var result = await storage.MoveToCheckInStorageAsync("upload.jpg", photoId, Guid.NewGuid(),
            TestContext.Current.CancellationToken, BucketType.CheckIn);
        result.Should().Be($"check-ins/{photoId:N}.jpg");
        targetBytes.Should().Be("already verified");
        client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StorageFailureDuringDestinationStat_FailsClosedWithoutOverwriting()
    {
        var client = new Mock<IMinioClient>();
        client.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.StatObjectAsync(It.IsAny<StatObjectArgs>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MinioException("storage unavailable"));
        using var storage = new MinIOStorageService(new MinIOOptions(), client.Object);
        await FluentActions.Awaiting(() => storage.MoveToCheckInStorageAsync("upload.jpg", Guid.NewGuid(), Guid.NewGuid(),
            TestContext.Current.CancellationToken, BucketType.CheckIn)).Should().ThrowAsync<MinioException>();
        client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
