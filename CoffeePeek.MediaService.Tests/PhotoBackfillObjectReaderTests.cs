using System.Net;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillObjectReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadinessIsRequiredAndReadyReadUsesExactlyOneObjectGetWithoutSdkHeadsOrMutators()
    {
        var clock = new Clock(); var state = new CheckInStorageState(clock);
        var minio = Client(); var handler = new Handler(); using var http = new HttpClient(handler);
        using var reader = new PhotoBackfillObjectReader(minio.Object, "private-bucket", http, state);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct));
        minio.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Never); Assert.Empty(handler.Requests);
        state.MarkVerified(clock.GetTimestamp());
        await using (var opened = await reader.OpenAsync("private-key", Ct))
        {
            using var buffer = new MemoryStream(); await opened.Body.CopyToAsync(buffer, Ct);
            Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray()); Assert.Equal(3, opened.Length);
        }
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        minio.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Once);
        minio.VerifyNoOtherCalls(); // Strict mock rejects HEAD, policy mutation, tags, copy, delete and PUT.
        clock.Advance(CheckInStorageState.MaximumAge);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct)); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "<Error><Code>NoSuchKey</Code></Error>", true)]
    [InlineData(HttpStatusCode.NotFound, "<Error><Code>NoSuchBucket</Code></Error>", false)]
    [InlineData(HttpStatusCode.NotFound, "<Error><Code>AccessDenied</Code></Error>", false)]
    [InlineData(HttpStatusCode.NotFound, "not an XML storage response", false)]
    [InlineData(HttpStatusCode.Forbidden, "<Error><Code>NoSuchKey</Code></Error>", false)]
    [InlineData(HttpStatusCode.TemporaryRedirect, "<Error><Code>NoSuchKey</Code></Error>", false)]
    public async Task OnlyConfirmedNoSuchKey404MeansMissingAndOtherResponsesAreSanitized(HttpStatusCode status, string body, bool missing)
    {
        var state = Ready(); var minio = Client(); var handler = new Handler { Status = status, ErrorBody = body };
        using var http = new HttpClient(handler); using var reader = new PhotoBackfillObjectReader(minio.Object, "private-bucket", http, state);
        var error = await Record.ExceptionAsync(() => reader.OpenAsync("private-key", Ct));
        if (missing) Assert.IsType<PhotoBackfillObjectMissingException>(error);
        else Assert.IsType<ServiceUnavailableException>(error);
        Assert.NotNull(error); Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-key", error.ToString()); Assert.DoesNotContain("server-only", error.ToString());
        Assert.DoesNotContain("storage.test", error.ToString()); Assert.DoesNotContain("X-Amz-", error.ToString());
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        minio.Verify(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()), Times.Once); minio.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownResponseLengthRemainsUnknownUntilTheCallerReadsBody()
    {
        var minio = Client(); var handler = new Handler { NoLength = true }; using var http = new HttpClient(handler);
        using var reader = new PhotoBackfillObjectReader(minio.Object, "private-bucket", http, Ready());
        await using var opened = await reader.OpenAsync("private-key", Ct);
        Assert.Null(opened.Length); using var bytes = new MemoryStream(); await opened.Body.CopyToAsync(bytes, Ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray()); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OversizedOrDtdXmlErrorDoesNotBecomeMissingAndIsReadUnderSmallByteCap()
    {
        foreach (var body in new[]
        {
            "<Error><Code>NoSuchKey</Code><Message>" + new string('x', 8192) + "</Message></Error>",
            "<!DOCTYPE Error [<!ENTITY secret SYSTEM 'file:///private'>]><Error><Code>&secret;</Code></Error>"
        })
        {
            var handler = new Handler { Status = HttpStatusCode.NotFound, ErrorBody = body, NoLength = true }; var minio = Client();
            using var http = new HttpClient(handler); using var reader = new PhotoBackfillObjectReader(minio.Object, "private-bucket", http, Ready());
            await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct)); Assert.Single(handler.Requests);
            Assert.InRange(handler.Body!.ReadBytes, 1, 4097);
        }
    }

    [Fact]
    public async Task HttpAndSigningFailuresHideSourceAndTimeoutWhileExplicitCancellationRemainsCancellation()
    {
        var minio = Client(); var handler = new Handler { Error = new HttpRequestException("private-key https://storage.test?X-Amz-Signature=server-only") };
        using var http = new HttpClient(handler); using var reader = new PhotoBackfillObjectReader(minio.Object, "private-bucket", http, Ready());
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct));
        Assert.Null(error.InnerException); Assert.DoesNotContain("server-only", error.ToString());
        handler.Error = new TaskCanceledException("private signed request timed out");
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.OpenAsync("private-key", cancelled.Token));
        Assert.Equal(2, handler.Requests.Count);

        var signing = Client(); signing.Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ThrowsAsync(new InvalidOperationException("private-key server-only"));
        using var failedReader = new PhotoBackfillObjectReader(signing.Object, "private-bucket", http, Ready());
        var signedError = await Assert.ThrowsAsync<ServiceUnavailableException>(() => failedReader.OpenAsync("private-key", Ct));
        Assert.Null(signedError.InnerException); Assert.DoesNotContain("private-key", signedError.ToString()); Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DefaultTransportRejectsRedirectWithoutContactingItsDestination()
    {
        var redirectCalls = 0; var targetCalls = 0;
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/private-bucket/private-key", (Microsoft.AspNetCore.Http.HttpContext context) =>
        { redirectCalls++; context.Response.StatusCode = 307; context.Response.Headers.Location = "/target"; });
        app.MapGet("/target", () => { targetCalls++; return "secret pixels"; }); await app.StartAsync(Ct);
        try
        {
            // Exercise the public constructor and its production transport using locally signed GETs.
            using var reader = new PhotoBackfillObjectReader(Options.Create(new MinIOOptions
                { Endpoint = app.Urls.Single(), CheckInBucketName = "private-bucket", AccessKey = "writer" }),
                Options.Create(new CheckInPhotoReadOptions { AccessKey = "test-reader", SecretKey = "test-only-secret" }), Ready());
            await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.OpenAsync("private-key", Ct));
            Assert.Equal(1, redirectCalls); Assert.Equal(0, targetCalls);
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }

    private static Mock<IMinioClient> Client()
    {
        var client = new Mock<IMinioClient>(MockBehavior.Strict);
        client.Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ReturnsAsync("https://storage.test/private-key?X-Amz-Signature=server-only"); client.Setup(c => c.Dispose()); return client;
    }

    private static CheckInStorageState Ready()
    { var state = new CheckInStorageState(TimeProvider.System); state.MarkVerified(TimeProvider.System.GetTimestamp()); return state; }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ErrorBody { get; init; } = "storage error";
        public bool NoLength { get; init; }
        public CountedStream? Body { get; private set; }
        public Exception? Error { get; set; }
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Requests.Add((request.Method, request.RequestUri!)); Assert.True(OpenTelemetry.Sdk.SuppressInstrumentation);
            if (Error is not null) return Task.FromException<HttpResponseMessage>(Error);
            var bytes = Status == HttpStatusCode.OK ? new byte[] { 1, 2, 3 } : System.Text.Encoding.UTF8.GetBytes(ErrorBody);
            var response = new HttpResponseMessage(Status) { Content = NoLength
                ? new StreamContent(Body = new CountedStream(bytes)) : new ByteArrayContent(bytes) };
            if ((int)Status is >= 300 and < 400) response.Headers.Location = new Uri("https://other.test/private"); return Task.FromResult(response);
        }
    }

    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public int ReadBytes { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var count = await base.ReadAsync(buffer, ct); ReadBytes += count; return count; }
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan delta) => timestamp += delta.Ticks;
    }
}
