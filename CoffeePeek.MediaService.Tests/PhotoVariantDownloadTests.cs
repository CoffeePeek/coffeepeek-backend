using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantDownloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", false, 200, 1)]
    [InlineData("HEAD", false, 200, 0)]
    [InlineData("GET", true, 304, 0)]
    [InlineData("HEAD", true, 304, 0)]
    public async Task HeadAndWeakConditionalUseReadyMetadataWithoutReadingTheObject(string method, bool conditional, int status, int reads)
    {
        await using var fixture = new Fixture(); await fixture.Seed();
        var controller = fixture.Controller(method);
        if (conditional) controller.Request.Headers.IfNoneMatch = "\"other\", W/" + fixture.ETag;
        var result = await fixture.Download(controller);
        Assert.Equal(status, result is StatusCodeResult code ? code.StatusCode : controller.Response.StatusCode);
        Assert.Equal(fixture.ETag, controller.Response.Headers.ETag);
        Assert.Equal("image/webp", controller.Response.ContentType);
        Assert.Equal(123, controller.Response.ContentLength);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        fixture.Reader.Verify(r => r.CopyToAsync(fixture.Variant.ObjectKey, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(reads));
        fixture.Reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("untrusted")]
    [InlineData("wrong-owner")]
    [InlineData("source-changed")]
    [InlineData("deleted")]
    [InlineData("tombstone")]
    [InlineData("cancelled")]
    public async Task ConditionalCannotBypassBindingStatusOrInternalAuthentication(string change)
    {
        await using var fixture = new Fixture(); await fixture.Seed();
        var controller = fixture.Controller("GET"); controller.Request.Headers.IfNoneMatch = "*";
        switch (change)
        {
            case "untrusted": controller.Request.Headers.Remove(GatewayHeaderConsts.XGatewayAuth); break;
            case "wrong-owner": fixture.User.Setup(u => u.GetUserId()).Returns(Guid.NewGuid()); break;
            case "source-changed": fixture.Photo.StorageKey += "new"; break;
            case "deleted": fixture.Photo.Status = PhotoStatus.Deleted; break;
            case "tombstone": fixture.Db.CheckInPhotoTombstones.Add(new CheckInPhotoTombstone { CheckInId = fixture.Job.CheckInId }); break;
            case "cancelled": fixture.Job.Status = PhotoVariantJobStatus.Cancelled; break;
        }
        await fixture.Db.SaveChangesAsync(Ct);
        var result = await fixture.Download(controller);
        Assert.Equal(change == "untrusted" ? 401 : 404, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        fixture.Reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedPolicyAuditDeniesEven304WithoutObjectRead()
    {
        await using var fixture = new Fixture(); await fixture.Seed();
        fixture.State.Lease.Invalidate();
        var controller = fixture.Controller("GET"); controller.Request.Headers.IfNoneMatch = "*";
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => fixture.Download(controller));
        fixture.Reader.VerifyNoOtherCalls();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly MediaDbContext Db = new(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Mock<IPhotoVariantReader> Reader = new();
        public readonly Mock<IUserContext> User = new();
        public readonly PhotoVariantReadState State = new(TimeProvider.System);
        public readonly PhotoMetadata Photo = new()
        {
            Id = Guid.NewGuid(), CheckInId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Status = PhotoStatus.Confirmed,
            BucketType = BucketType.CheckIn, StorageKey = "private/original", FileName = "source.jpg", ContentType = "image/jpeg"
        };
        public PhotoVariantJob Job = null!;
        public PhotoVariant Variant = null!;
        public string ETag => CheckInPhotoVariantIdentity.ETag(Job.SourceVersion, Job.RecipeVersion, Variant.Digest);
        public async Task Seed()
        {
            Job = new PhotoVariantJob { Id = Guid.NewGuid(), CheckInId = Photo.CheckInId!.Value, PhotoId = Photo.Id,
                OwnerId = Photo.OwnerId, SourceKey = Photo.StorageKey, SourceVersion = CheckInPhotoVariantIdentity.SourceVersion(Photo.Id, Photo.StorageKey),
                RecipeVersion = 1, Status = PhotoVariantJobStatus.Ready, LeaseToken = Guid.NewGuid() };
            Variant = PhotoVariantPersistenceTests.Manifest(Job).Single(v => v.Purpose == "feed" && v.Format == "webp");
            Db.Photos.Add(Photo); Db.PhotoVariantJobs.Add(Job); Db.PhotoVariants.Add(Variant); await Db.SaveChangesAsync(Ct);
            State.Lease.MarkVerified(TimeProvider.System.GetTimestamp());
            User.Setup(u => u.GetUserId()).Returns(Photo.OwnerId);
        }
        public InternalCheckInPhotoVariantsController Controller(string method)
        {
            var controller = new InternalCheckInPhotoVariantsController(new PhotoVariantReads(Db), State,
                Options.Create(new PhotoVariantOptions { ReadEnabled = true }), User.Object,
                Options.Create(new GatewayAuthOptions { SecretKey = "test-secret" }))
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controller.Request.Method = method; controller.Response.Body = new MemoryStream();
            controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "test-secret";
            return controller;
        }
        public async Task<IActionResult> Download(InternalCheckInPhotoVariantsController controller)
        {
            using var services = new ServiceCollection().AddSingleton(Reader.Object).BuildServiceProvider();
            return await controller.Download(Job.CheckInId, Photo.Id, Job.SourceVersion, 1, "feed", "webp", services, Ct);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
