using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class InternalCheckInPhotoAccessTests
{
    [Fact]
    public async Task UntrustedRequest_DoesNotReadMetadataOrStorage()
    {
        var repo = new Mock<IPhotoRepository>(); var storage = new Mock<ICheckInPhotoReader>();
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(Guid.NewGuid());
        var controller = Create(repo, storage, user);
        (await controller.Download(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken))
            .Should().BeOfType<UnauthorizedResult>();
        repo.VerifyNoOtherCalls(); storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TrustedRequestWithoutMatchingParent_DoesNotServeBytes()
    {
        var repo = new Mock<IPhotoRepository>(); var storage = new Mock<ICheckInPhotoReader>();
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(Guid.NewGuid());
        var controller = Create(repo, storage, user);
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "secret";
        (await controller.Download(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken))
            .Should().BeOfType<NotFoundResult>();
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MatchingParent_StreamsPrivateBytesWithoutRedirectOrPublicUrl()
    {
        var owner = Guid.NewGuid(); var checkIn = Guid.NewGuid(); var id = Guid.NewGuid();
        var repo = new Mock<IPhotoRepository>(); var storage = new Mock<ICheckInPhotoReader>();
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(owner);
        repo.Setup(r => r.GetCheckInPhotoAsync(checkIn, id, owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PhotoMetadata { StorageKey = "private", ContentType = "image/jpeg" });
        storage.Setup(s => s.CopyToAsync("private", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>((_, stream, ct) => stream.WriteAsync(new byte[] { 1, 2, 3 }, ct).AsTask());
        var controller = Create(repo, storage, user);
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "secret";
        (await controller.Download(checkIn, id, TestContext.Current.CancellationToken)).Should().BeOfType<EmptyResult>();
        controller.Response.Headers.CacheControl.ToString().Should().Be("private, no-store");
        controller.Response.ContentType.Should().Be("image/jpeg");
        controller.Response.Headers.ContentDisposition.Should().BeEmpty();
        ((MemoryStream)controller.Response.Body).ToArray().Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public async Task HistoricalUnsafeMime_IsStreamedOnlyAsAnOctetStreamAttachment(string contentType)
    {
        var owner=Guid.NewGuid(); var checkIn=Guid.NewGuid(); var id=Guid.NewGuid();
        var repo=new Mock<IPhotoRepository>(); var storage=new Mock<ICheckInPhotoReader>();
        var user=new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(owner);
        repo.Setup(r => r.GetCheckInPhotoAsync(checkIn, id, owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PhotoMetadata { StorageKey="legacy", ContentType=contentType });
        storage.Setup(s => s.CopyToAsync("legacy", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string,Stream,CancellationToken>((_,stream,ct) =>
                stream.WriteAsync("<script>alert(1)</script>"u8.ToArray(),ct).AsTask());
        var controller=Create(repo,storage,user);
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth]="secret";
        (await controller.Download(checkIn,id,TestContext.Current.CancellationToken)).Should().BeOfType<EmptyResult>();
        controller.Response.ContentType.Should().Be("application/octet-stream");
        controller.Response.Headers.ContentDisposition.ToString().Should().Be("attachment");
        controller.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        ((MemoryStream)controller.Response.Body).Length.Should().BeGreaterThan(0);
    }

    private static InternalCheckInPhotosController Create(Mock<IPhotoRepository> repo, Mock<ICheckInPhotoReader> storage, Mock<IUserContext> user)
    {
        var controller = new InternalCheckInPhotosController(new ClaimCheckInPhotosHandler(repo.Object, Mock.Of<IStorageService>(),
            Mock.Of<IUnitOfWork>()), new CopyShelfBagCardHandler(repo.Object, Mock.Of<IStorageService>(), Mock.Of<IUnitOfWork>()),
            repo.Object, storage.Object, user.Object,
            Options.Create(new GatewayAuthOptions { SecretKey = "secret" }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Response.Body = new MemoryStream();
        return controller;
    }
}
