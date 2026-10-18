using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillCandidateClientTests
{
    private const string Secret = "test-private-inventory-service-secret";
    private const int MaxBytes = 1024 * 1024;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task PostsFrozenKeysetRequestWithOnlyServiceAuthenticationAndDeserializesEnumDatesAndCursor()
    {
        var date = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var candidate = Candidate(date);
        var cursor = new PhotoBackfillCursor(date, candidate.CheckInId, candidate.PhotoId);
        var expected = new PhotoBackfillPage([candidate], cursor);
        var request = Request() with { Cursor = cursor, Limit = 7 };
        var handler = new Handler(async (message, _) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("http://shops.internal/internal/check-in-photo-backfill/candidates", message.RequestUri!.AbsoluteUri);
            Assert.Equal(Secret, Assert.Single(message.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth)));
            Assert.Null(message.Headers.Authorization);
            Assert.False(message.Headers.Contains(GatewayHeaderConsts.XUserId));
            var posted = JsonSerializer.Deserialize<PhotoBackfillPageRequest>(await message.Content!.ReadAsStringAsync(Ct), Json);
            Assert.Equal(request, posted);
            return Response(JsonSerializer.Serialize(expected, Json));
        });
        using var http = Http(handler);
        var actual = await Client(http).ReadAsync(request, Ct);
        Assert.Equal(candidate, Assert.Single(actual.Items));
        Assert.Equal(cursor, actual.NextCursor);
        Assert.Equal(CheckInKind.Home, actual.Items[0].Kind);
        Assert.Equal(DateTimeKind.Utc, actual.Items[0].SortAtUtc.Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FailureStatusIsSanitizedBeforeReadingPotentiallySensitiveBody(HttpStatusCode status)
    {
        const string privateError = "storage=check-ins/private-original.jpg secret=test-private-inventory-service-secret";
        var stream = new CountingStream(Encoding.UTF8.GetBytes(privateError));
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
            { Content = new StreamContent(stream) })));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request(), Ct));
        AssertSanitized(error);
        Assert.Equal(0, stream.BytesRead);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData("{not-json storage=private-original}")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    public async Task InvalidOrNullPayloadHasNoRawJsonInError(string body)
    {
        using var http = Http(new Handler((_, _) => Task.FromResult(Response(body))));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request(), Ct));
        AssertSanitized(error);
    }

    [Fact]
    public async Task MoreCandidatesThanRequestedFailWithoutReturningAPartialPage()
    {
        var page = new PhotoBackfillPage([Candidate(DateTime.UtcNow), Candidate(DateTime.UtcNow.AddSeconds(-1))], null);
        using var http = Http(new Handler((_, _) => Task.FromResult(Response(JsonSerializer.Serialize(page, Json)))));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request() with { Limit = 1 }, Ct));
        AssertSanitized(error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BodyBudgetIsEnforcedBeforeAnyHttpClientPrebuffering(bool contentLengthKnown)
    {
        var stream = new CountingStream(new byte[MaxBytes * 4]);
        var content = new StreamContent(stream);
        if (contentLengthKnown) content.Headers.ContentLength = MaxBytes * 4;
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request(), Ct));
        AssertSanitized(error);
        // ResponseContentRead would consume all four MiB before the client could reject it.
        if (contentLengthKnown) Assert.Equal(0, stream.BytesRead);
        else Assert.InRange(stream.BytesRead, MaxBytes + 1, MaxBytes + 16 * 1024);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ExactlyOneMebibyteOfValidJsonIsAccepted()
    {
        const string page = "{\"items\":[],\"nextCursor\":null}";
        var stream = new CountingStream(Encoding.UTF8.GetBytes(page.PadRight(MaxBytes)));
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(stream) })));
        var result = await Client(http).ReadAsync(Request(), Ct);
        Assert.Empty(result.Items); Assert.Null(result.NextCursor);
        Assert.Equal(MaxBytes, stream.BytesRead); Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task UnavailableTransportAndItsSensitiveInnerErrorAreSanitized()
    {
        using var http = Http(new Handler((_, _) => throw new HttpRequestException(
            "http://shops.internal/private-original?secret=" + Secret)));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request(), Ct));
        AssertSanitized(error);
    }

    [Fact]
    public async Task CallerCancellationDuringStreamReadRemainsCancellation()
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var stream = new WaitingStream();
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(stream) })));
        var read = Client(http).ReadAsync(Request(), canceled.Token);
        await stream.Started.Task.WaitAsync(Ct);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task TransportTimeoutWithoutCallerCancellationIsAnAvailabilityFailure()
    {
        using var http = Http(new Handler((_, _) => throw new OperationCanceledException("timeout private-original " + Secret)));
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Client(http).ReadAsync(Request(), Ct));
        AssertSanitized(error);
    }

    private static void AssertSanitized(ServiceUnavailableException error)
    {
        Assert.Equal("Community photo inventory is unavailable.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain("private-original", error.ToString());
        Assert.DoesNotContain("shops.internal", error.ToString());
    }

    private static PhotoBackfillPageRequest Request() => new(PhotoBackfillPhase.RecentPublicShop,
        new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), null, 10);

    private static PhotoBackfillCandidate Candidate(DateTime date) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "check-ins/private-original.png", new string('a', 64), CheckInKind.Home, true, date, 1200, "image/png");

    private static PhotoBackfillCandidateClient Client(HttpClient http) => new(http,
        Options.Create(new GatewayAuthOptions { SecretKey = Secret }));
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://shops.internal/base/") };
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = base.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class WaitingStream : MemoryStream
    {
        public override bool CanSeek => false;
        public bool Disposed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
