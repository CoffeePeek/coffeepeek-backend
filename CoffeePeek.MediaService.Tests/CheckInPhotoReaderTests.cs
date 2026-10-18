using System.Net;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Exceptions;
using Minio;
using Minio.DataModel.Args;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInPhotoReaderTests
{
    [Fact]
    public async Task ReadyDownloadSignsThenMakesOneObjectGetAndExpiredReadinessDoesNeither()
    {
        var clock = new ManualClock();
        var state = new CheckInStorageState(clock);
        var client = new Mock<IMinioClient>(MockBehavior.Strict);
        client.Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ReturnsAsync("https://storage.test/private-bucket/key?X-Amz-Signature=server-only");
        client.Setup(c => c.Dispose());
        var handler = new ObjectHandler();
        using var http = new HttpClient(handler);
        using var reader = new CheckInPhotoReader(client.Object, "private-bucket", state, http);
        using var output = new MemoryStream();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
        client.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Never);
        Assert.Empty(handler.Requests);

        state.MarkVerified(clock.GetTimestamp());
        await reader.CopyToAsync("key", output, ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        client.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Once);
        client.VerifyNoOtherCalls(); // No SDK bucket/object HEAD, policy call or object write.
        clock.Advance(CheckInStorageState.MaximumAge);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
        client.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Once);
        Assert.Single(handler.Requests);
        state.MarkVerified(clock.GetTimestamp()); state.Invalidate();
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PinnedSdkSignsLocallyAndEscapesTheKeyWithoutHiddenHeadOrRegionRequests()
    {
        var state = new CheckInStorageState(TimeProvider.System); state.MarkVerified(TimeProvider.System.GetTimestamp());
        var handler = new ObjectHandler();
        using var http = new HttpClient(handler);
        var client = new MinioClient().WithEndpoint("storage.test:9000").WithCredentials("test-reader", "test-secret")
            .WithRegion("us-east-1").WithHttpClient(http).WithSSL(false).Build();
        using var reader = new CheckInPhotoReader(client, "private-bucket", state, http);
        using var output = new MemoryStream();
        const string key = "check-ins/folder/a photo+100%#?.jpg";
        await reader.CopyToAsync(key, output, TestContext.Current.CancellationToken);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/private-bucket/" + key, Uri.UnescapeDataString(request.Uri.AbsolutePath));
        Assert.Empty(request.Uri.Fragment);
        Assert.Contains("X-Amz-Expires=60", request.Uri.Query);
        Assert.Contains("X-Amz-Signature=", request.Uri.Query);
        Assert.Contains("us-east-1", Uri.UnescapeDataString(request.Uri.Query));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task StorageRedirectsAndErrorBodiesAreNotFollowedOrCopied(HttpStatusCode status)
    {
        var state = new CheckInStorageState(TimeProvider.System); state.MarkVerified(TimeProvider.System.GetTimestamp());
        var handler = new ObjectHandler { Status = status };
        using var http = new HttpClient(handler);
        var client = new MinioClient().WithEndpoint("storage.test:9000").WithCredentials("test-reader", "test-secret")
            .WithRegion("us-east-1").WithHttpClient(http).WithSSL(false).Build();
        using var reader = new CheckInPhotoReader(client, "private-bucket", state, http);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            reader.CopyToAsync("private-key.jpg", output, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
        Assert.Empty(output.ToArray());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-key", error.ToString());
        Assert.DoesNotContain("X-Amz-", error.ToString());
        Assert.DoesNotContain("storage.test", error.ToString());
    }

    [Fact]
    public async Task TransportExceptionDoesNotExposeSignedUrlAndCallerCancellationIsPreserved()
    {
        var state = new CheckInStorageState(TimeProvider.System); state.MarkVerified(TimeProvider.System.GetTimestamp());
        var client = new Mock<IMinioClient>();
        client.Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ReturnsAsync("https://storage.test/private-key?X-Amz-Signature=server-only");
        var handler = new ObjectHandler { Throw = true };
        using var http = new HttpClient(handler);
        using var reader = new CheckInPhotoReader(client.Object, "private-bucket", state, http);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            reader.CopyToAsync("private-key", output, TestContext.Current.CancellationToken));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("server-only", error.ToString());
        Assert.DoesNotContain("storage.test", error.ToString());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CopyToAsync("private-key", output, cancelled.Token));
        client.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Once);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void SlowAuditCannotRestartItsFreshnessBudgetAtCompletion()
    {
        var clock = new ManualClock();
        var state = new CheckInStorageState(clock);
        var start = clock.GetTimestamp();
        clock.Advance(CheckInStorageState.MaximumAge);
        state.MarkVerified(start);
        Assert.False(state.IsReady);
    }

    private sealed class ObjectHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public bool Throw { get; init; }
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!));
            Assert.True(OpenTelemetry.Sdk.SuppressInstrumentation);
            if (Throw) throw new HttpRequestException("Failed private request: " + request.RequestUri);
            var response = new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.OK ? new ByteArrayContent([1, 2, 3])
                    : new StringContent("<Error>private-key X-Amz-Signature=server-only</Error>")
            };
            if ((int)Status is >= 300 and <= 399) response.Headers.Location = new Uri("https://other-storage.test/private");
            return Task.FromResult(response);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
