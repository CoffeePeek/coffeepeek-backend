using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;
public class OwnershipEvidenceUploadTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Mock<IStorageService> storage = new();
    private readonly Mock<IPhotoRepository> photos = new();
    private readonly Mock<IUnitOfWork> unit = new();
    private OwnershipEvidenceUploadsController Controller()
    {
        var user = new Mock<IUserContext>(); user.Setup(x => x.GetUserIdOrThrow()).Returns(owner);
        return new(user.Object, photos.Object, storage.Object, unit.Object)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }
    private static FormFile File(byte[] bytes, string type = "image/png") =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", "proof.png")
        { Headers = new HeaderDictionary(), ContentType = type };

    [Fact]
    public async Task ValidImageIsPrivateTemporaryAndScopedToTheAuthenticatedUser()
    {
        using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var controller = Controller();
        Assert.IsType<OkObjectResult>(await controller.Upload(File(data.ToArray()), CancellationToken.None));
        photos.Verify(x => x.Add(It.Is<PhotoMetadata>(p => p.OwnerId == owner
            && p.OwnerType == OwnerType.OwnershipEvidence && p.BucketType == BucketType.CheckIn
            && p.Status == PhotoStatus.Pending && p.OwnershipClaimId == null)));
        storage.Verify(x => x.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.MarkAsTemporary(It.Is<string>(key => key.StartsWith("ownership-evidence/")),
            BucketType.CheckIn, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
    }
    [Theory]
    [InlineData("image/png")]
    [InlineData("text/html")]
    public async Task ArbitraryBytesAndDisallowedMediaTypesAreRejectedBeforeStorage(string type)
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().Upload(File([1, 2, 3], type), CancellationToken.None));
        Assert.Empty(storage.Invocations);
        Assert.Empty(photos.Invocations);
    }
    [Fact]
    public async Task OversizedFilesAreRejectedWithoutReadingOrStoring()
    {
        var file = new FormFile(Stream.Null, 0, OwnershipEvidenceUploadsController.MaxPhotoBytes + 1, "file", "proof.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Assert.IsType<BadRequestObjectResult>(await Controller().Upload(file, CancellationToken.None));
        Assert.Empty(storage.Invocations);
    }
}
