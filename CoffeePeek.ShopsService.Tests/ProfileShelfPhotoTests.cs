using System.Net;
using System.Net.Http.Headers;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public sealed class ProfileShelfPhotoTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid bag = Guid.NewGuid();
    private readonly Guid photo = Guid.NewGuid();
    private readonly Mock<IProfileAddressLookup> profiles = new();
    private readonly Mock<IShelfProfileStore> shelf = new();

    private ProfileShelfController Controller(PhotoHandler handler, bool visibleAfterMedia = true, bool profileAfterMedia = true)
    {
        var profile = new PublicAddressMetadata(owner, "author", "/users/author", 1);
        var profileReads = 0;
        profiles.Setup(p => p.ResolveAsync("author", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++profileReads == 1 || profileAfterMedia ? profile : null);
        var reads = 0;
        shelf.Setup(s => s.PublicPhotoAsync(owner, bag, photo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++reads == 1 || visibleAfterMedia ? new ShelfBagPhotoAccess(owner, bag, photo, "image/jpeg") : null);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler) { BaseAddress = new Uri("http://media.test") });
        var media = new MediaShelfBagPhotoClient(clients.Object, Options.Create(new GatewayAuthOptions { SecretKey = "fake-test-secret" }),
            NullLogger<MediaShelfBagPhotoClient>.Instance);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        return new(profiles.Object, shelf.Object, media) { ControllerContext = new() { HttpContext = context } };
    }

    [Fact]
    public async Task ValidPublicCardIsProxiedWithoutCachingAndInternalOwnerIsNotExposed()
    {
        var handler = new PhotoHandler(); var controller = Controller(handler);
        controller.Request.Headers.IfNoneMatch = "*";
        Assert.IsType<EmptyResult>(await controller.Photo("author", bag, photo, TestContext.Current.CancellationToken));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("image/jpeg", controller.Response.ContentType);
        Assert.Empty(controller.Response.Headers.ETag.ToString());
        Assert.Equal(3, controller.Response.Body.Length);
        Assert.Equal($"/internal/shelf-bag-photos/{bag}/{photo}", handler.Path);
        Assert.Equal(owner.ToString(), handler.OwnerHeader);
        Assert.Equal(1, handler.Calls);
        shelf.Verify(s => s.PublicPhotoAsync(owner, bag, photo, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OptOutOrProfileBlockDuringMediaReadPreventsAnyPublicBytes(bool visible, bool profileVisible)
    {
        var handler = new PhotoHandler(); var controller = Controller(handler, visible, profileVisible);
        Assert.IsType<NotFoundResult>(await controller.Photo("author", bag, photo, TestContext.Current.CancellationToken));
        Assert.Equal(0, controller.Response.Body.Length);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task HiddenOrFinishedOrReplacedCardDoesNotContactMedia()
    {
        var handler = new PhotoHandler(); var controller = Controller(handler);
        shelf.Setup(s => s.PublicPhotoAsync(owner, bag, photo, It.IsAny<CancellationToken>())).ReturnsAsync((ShelfBagPhotoAccess?)null);
        Assert.IsType<NotFoundResult>(await controller.Photo("author", bag, photo, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("text/html", 3)]
    [InlineData("image/png", 3)]
    [InlineData("image/jpeg", 1048577)]
    public async Task InvalidMediaTypeOrSizeIsRejectedBeforeWriting(string contentType, int length)
    {
        var controller = Controller(new PhotoHandler(contentType, length));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Photo("author", bag, photo,
            TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, controller.Response.Body.Length);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoAnAvailabilityResponse()
    {
        var controller = Controller(new PhotoHandler());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Photo("author", bag, photo, cancelled.Token));
        Assert.Equal(0, controller.Response.Body.Length);
    }

    private sealed class PhotoHandler(string contentType = "image/jpeg", int length = 3) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? OwnerHeader { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            Path = request.RequestUri!.AbsolutePath;
            OwnerHeader = request.Headers.GetValues("X-User-Id").Single();
            var body = new ByteArrayContent(new byte[length]);
            body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }
}
