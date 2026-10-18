using System.Diagnostics.Metrics;
using CoffeePeek.Shared.Kernel.Observability;

namespace CoffeePeek.MediaService.Tests;

public class PhotoDeliveryTelemetryTests
{
    [Fact]
    public async Task StreamingReportsTransferredBytesAndBoundedDimensionsWithoutDisposingStreams()
    {
        var measurements = new List<(long Bytes, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PhotoDeliveryTelemetry.InstrumentationName && instrument.Name == "photo.stream.bytes")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copy = tags.ToArray();
            if (copy.Any(t => t.Key == "stage" && Equals(t.Value, "test_stream")))
                measurements.Add((value, copy));
        });
        listener.Start();
        var bytes = new byte[150_000]; Random.Shared.NextBytes(bytes);
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await PhotoDeliveryTelemetry.CopyAsync(input, output, "test_stream", TestContext.Current.CancellationToken);
        Assert.Equal(bytes, output.ToArray());
        var recorded = Assert.Single(measurements);
        Assert.Equal(bytes.Length, recorded.Bytes);
        Assert.Equal(new[] { "stage", "outcome" }, recorded.Tags.Select(t => t.Key));
        Assert.Contains(recorded.Tags, t => t.Key == "outcome" && Equals(t.Value, "complete"));
        Assert.True(input.CanRead); Assert.True(output.CanWrite);
    }
}
