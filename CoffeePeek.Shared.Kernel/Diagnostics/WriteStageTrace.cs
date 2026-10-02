using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CoffeePeek.Shared.Kernel.Diagnostics;

/// <summary>Low-cardinality timing for work that delays a user-facing create response.</summary>
public static class WriteStageTrace
{
    public const string SourceName = "CoffeePeek.UserWrites";

    private static readonly ActivitySource Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "coffeepeek.user_write.stage.duration", "ms");

    public static async Task<T> MeasureAsync<T>(string operation, string stage, Func<Task<T>> action)
    {
        using var activity = Source.StartActivity($"{operation}.{stage}");
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";
        try
        {
            return await action();
        }
        catch
        {
            outcome = "error";
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            Duration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("stage", stage),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    public static Task MeasureAsync(string operation, string stage, Func<Task> action) =>
        MeasureAsync<object?>(operation, stage, async () =>
        {
            await action();
            return null;
        });
}
