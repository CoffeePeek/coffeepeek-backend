using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CoffeePeek.Shared.Kernel.Observability;

/// <summary>Only bounded stages, reader classes and outcomes; never photo keys, URLs or identities.</summary>
public static class PhotoDeliveryTelemetry
{
    public const string InstrumentationName = "CoffeePeek.PhotoDelivery";
    internal static readonly ActivitySource Source = new(InstrumentationName);
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("photo.stage.duration", "s");
    private static readonly Histogram<long> Bytes = Meter.CreateHistogram<long>("photo.stream.bytes", "By");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("photo.responses");

    public static PhotoDeliveryMeasurement Start(string stage, string reader = "internal") => new(stage, reader);

    internal static void Record(string stage, string reader, string outcome, double seconds)
    {
        var tags = new TagList { { "stage", stage }, { "reader", reader }, { "outcome", outcome } };
        Duration.Record(seconds, tags);
    }

    public static void Response(string reader, int statusCode, bool revalidation = false, bool fresh = false) => Requests.Add(1,
        new TagList { { "reader", reader }, { "status", statusCode }, { "cache", fresh ? "private_fresh" : revalidation ? "private_revalidate" : "no_store" } });

    public static async Task CopyAsync(Stream source, Stream destination, string stage, CancellationToken ct)
    {
        using var measurement = Start(stage);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long bytes = 0;
        var outcome = "error";
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, 64 * 1024), ct)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                bytes += read;
            }
            outcome = "complete";
            measurement.Complete(outcome);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "cancelled";
            measurement.Complete(outcome);
            throw;
        }
        finally
        {
            // Clear pooled image bytes; count only bytes successfully written to the downstream stream.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            Bytes.Record(bytes, new TagList { { "stage", stage }, { "outcome", outcome } });
        }
    }
}
