using System.Net;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Media;

public sealed class MediaCollectionCoverClientTests
{
    private static CollectionCoverAccess Photo() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "image/jpeg");

    private static MediaCollectionCoverClient Client(HttpContent content)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(f => f.CreateClient(MediaUploadedPhotoLookup.HttpClientName))
            .Returns(() => new HttpClient(new CoverHandler(content)) { BaseAddress = new Uri("http://media.test") });
        return new(clients.Object, Options.Create(new GatewayAuthOptions { SecretKey = "cover-tests-secret" }),
            NullLogger<MediaCollectionCoverClient>.Instance);
    }

    [Fact]
    public async Task StalledBodyAfterSuccessfulHeadersIsCancelledAndDisposedWithinTheDownloadDeadline()
    {
        var content = new StalledContent();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(content).DownloadAsync(Photo(), TestContext.Current.CancellationToken));
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task CoverResponseLargerThanOneMebibyteIsRejectedBeforeItCanBeServed()
    {
        using var content = new ByteArrayContent(new byte[1048577]);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(content).DownloadAsync(Photo(), TestContext.Current.CancellationToken));
    }

    private sealed class CoverHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class StalledContent : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
