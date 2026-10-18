using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Exceptions;
using Minio;
using Minio.DataModel.Args;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInPhotoReaderTests
{
    [Fact]
    public async Task ReadyDownloadOnlyCallsObjectRead_AndExpiredReadinessFailsClosed()
    {
        var clock = new ManualClock();
        var state = new CheckInStorageState(clock);
        var client = new Mock<IMinioClient>();
        using var reader = new CheckInPhotoReader(client.Object, "private-bucket", state);
        using var output = new MemoryStream();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
        client.VerifyNoOtherCalls();

        state.MarkVerified(clock.GetTimestamp());
        await reader.CopyToAsync("key", output, ct);
        client.Verify(c => c.GetObjectAsync(It.IsAny<GetObjectArgs>(), ct), Times.Once);
        client.VerifyNoOtherCalls(); // No bucket HEAD, policy mutation or object write on this path.
        clock.Advance(CheckInStorageState.MaximumAge);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
        client.Verify(c => c.GetObjectAsync(It.IsAny<GetObjectArgs>(), ct), Times.Once);
        state.MarkVerified(clock.GetTimestamp());
        state.Invalidate();
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => reader.CopyToAsync("key", output, ct));
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

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
