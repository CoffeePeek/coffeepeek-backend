using System.Diagnostics;

namespace CoffeePeek.Shared.Kernel.Observability;

public sealed class PhotoDeliveryMeasurement : IDisposable
{
    private readonly string stage;
    private readonly string reader;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Activity? activity;
    private string outcome = "error";

    internal PhotoDeliveryMeasurement(string stage, string reader)
    {
        this.stage = stage;
        this.reader = reader;
        activity = PhotoDeliveryTelemetry.Source.StartActivity($"photo.{stage}");
        activity?.SetTag("reader", reader);
    }

    public void Complete(string result = "complete") => outcome = result;

    public void Dispose()
    {
        PhotoDeliveryTelemetry.Record(stage, reader, outcome, Stopwatch.GetElapsedTime(started).TotalSeconds);
        activity?.SetTag("outcome", outcome);
        if (outcome == "error") activity?.SetStatus(ActivityStatusCode.Error);
        activity?.Dispose();
    }
}
