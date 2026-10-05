using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CoffeePhotoImportTests
{
    [Fact]
    public async Task RepeatedPhoto_StoresOriginalBytesOnce_AndKeepsPermanentMetadata()
    {
        await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Mock<IStorageService>(MockBehavior.Strict);
        var token = new string('a', 32);
        var bytes = Convert.FromHexString("89504E470D0A1A0A010203");
        var key = "coffees/" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) + ".png";
        storage.SetupSequence(s => s.Exists(key, CoffeePeek.MediaService.Configuration.BucketType.Shop, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false).ReturnsAsync(true);
        byte[]? saved = null;
        storage.Setup(s => s.Put(key, "image/png", It.IsAny<Stream>(), bytes.Length, CoffeePeek.MediaService.Configuration.BucketType.Shop, It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, Stream stream, long _, CoffeePeek.MediaService.Configuration.BucketType _, CancellationToken ct) =>
            { using var copy = new MemoryStream(); await stream.CopyToAsync(copy, ct); saved = copy.ToArray(); });
        storage.Setup(s => s.MarkAsPermanent(key, CoffeePeek.MediaService.Configuration.BucketType.Shop, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var controller = new InternalCoffeePhotosController(storage.Object, db, Options.Create(new CoffeeImportAuthOptions { Token = token }))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers[CoffeeImportAuthOptions.Header] = token;
        async Task<CoffeePeek.Contract.Dtos.Coffee.StoredCoffeePhotoDto> Upload()
        {
            using var content = new MemoryStream(bytes);
            var file = new FormFile(content, 0, bytes.Length, "file", "photo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
            return Assert.IsType<CoffeePeek.Contract.Dtos.Coffee.StoredCoffeePhotoDto>(Assert.IsType<OkObjectResult>(await controller.Upload(file, TestContext.Current.CancellationToken)).Value);
        }
        var first = await Upload();
        Assert.Equal(first, await Upload());
        Assert.Equal(bytes, saved);
        var row = Assert.Single(await db.Photos.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PhotoStatus.Confirmed, row.Status);
        Assert.Equal(OwnerType.Coffee, row.OwnerType);
        Assert.Equal(key, row.StorageKey);
        storage.Verify(s => s.Put(key, "image/png", It.IsAny<Stream>(), bytes.Length, CoffeePeek.MediaService.Configuration.BucketType.Shop, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.MarkAsPermanent(key, CoffeePeek.MediaService.Configuration.BucketType.Shop, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ImportRequiresDedicatedToken_AndRejectsActiveContent()
    {
        using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        var storage = new Mock<IStorageService>(MockBehavior.Strict);
        var token = new string('a', 32);
        var controller = new InternalCoffeePhotosController(storage.Object, db, Options.Create(new CoffeeImportAuthOptions { Token = token }))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        using var content = new MemoryStream("<svg onload='alert(1)'/>"u8.ToArray());
        var file = new FormFile(content, 0, content.Length, "file", "coffee.svg")
        { Headers = new HeaderDictionary(), ContentType = "image/svg+xml" };
        Assert.IsType<UnauthorizedResult>(await controller.Upload(file, TestContext.Current.CancellationToken));
        controller.Request.Headers[CoffeeImportAuthOptions.Header] = token;
        Assert.IsType<BadRequestObjectResult>(await controller.Upload(file, TestContext.Current.CancellationToken));
        storage.VerifyNoOtherCalls();
        Assert.Equal(("image/png", "png"), CoffeePhotoFormat.Detect(Convert.FromHexString("89504E470D0A1A0A")));
        Assert.Equal(("image/jpeg", "jpg"), CoffeePhotoFormat.Detect(Convert.FromHexString("FFD8FF")));
        Assert.Null(CoffeePhotoFormat.Detect("<html>not an image</html>"u8));
    }
}
