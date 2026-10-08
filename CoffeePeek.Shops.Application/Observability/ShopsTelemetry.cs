using System.Diagnostics;
using System.Diagnostics.Metrics;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Application.Observability;

/// <summary>Bounded metric dimensions only; entity identifiers belong in traces and logs.</summary>
public static class ShopsTelemetry
{
    public const string InstrumentationName = "CoffeePeek.Shops";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> CacheInvalidationFailures = Meter.CreateCounter<long>(
        "shops.cache_invalidation.failures", description: "Durable cache invalidation attempts that failed.");
    private static readonly Counter<long> OcrTerminalJobs = Meter.CreateCounter<long>(
        "shops.ocr.jobs.completed", description: "OCR jobs entering a terminal state.");
    private static readonly Histogram<double> OcrJobAge = Meter.CreateHistogram<double>(
        "shops.ocr.jobs.age", unit: "s", description: "Time from OCR request to terminal state.");
    private static readonly Counter<long> MediaClaimFailures = Meter.CreateCounter<long>(
        "shops.media.claim.failures", description: "Media photo claims rejected or unavailable.");
    private static readonly Counter<long> WriteConflicts = Meter.CreateCounter<long>(
        "shops.write.conflicts", description: "Profile and menu concurrency conflicts.");
    private static readonly Counter<long> RedirectAnalyticsFallbacks = Meter.CreateCounter<long>(
        "shops.app_redirect.analytics_fallbacks", description: "Redirect analytics recorded inline after enqueue failure.");
    private static readonly Counter<long> RedirectAnalyticsUnconfirmedEvents = Meter.CreateCounter<long>(
        "shops.app_redirect.analytics_unconfirmed", description: "Redirect analytics attempts not confirmed by queue or inline write.");

    public static void ProfileCacheInvalidationFailed() =>
        CacheInvalidationFailures.Add(1, new KeyValuePair<string, object?>("source", "profile"));

    public static void CheckInCacheInvalidationFailed() =>
        CacheInvalidationFailures.Add(1, new KeyValuePair<string, object?>("source", "check_in"));

    public static void OcrJobFinished(MenuOcrJob job)
    {
        var outcome = job.Status switch
        {
            MenuOcrJobStatus.Applied => "applied",
            MenuOcrJobStatus.Stale => "stale",
            MenuOcrJobStatus.Failed => "failed",
            _ => null
        };
        if (outcome is null || job.CompletedAtUtc is not { } completedAt)
            return;

        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        OcrTerminalJobs.Add(1, tag);
        OcrJobAge.Record(Math.Max(0, (completedAt - job.RequestedAtUtc).TotalSeconds), tag);
    }

    public static void ShopPhotoClaimFailed(bool unavailable) =>
        MediaClaimFailures.Add(1,
            new KeyValuePair<string, object?>("kind", "shop"),
            new KeyValuePair<string, object?>("reason", unavailable ? "unavailable" : "rejected"));

    public static void CheckInPhotoClaimFailed(bool unavailable) =>
        MediaClaimFailures.Add(1,
            new KeyValuePair<string, object?>("kind", "check_in"),
            new KeyValuePair<string, object?>("reason", unavailable ? "unavailable" : "rejected"));

    public static void ProfileWriteConflict() =>
        WriteConflicts.Add(1, new KeyValuePair<string, object?>("aggregate", "profile"));

    public static void MenuWriteConflict() =>
        WriteConflicts.Add(1, new KeyValuePair<string, object?>("aggregate", "menu"));

    public static void RedirectAnalyticsFallbackUsed(string channel) =>
        RedirectAnalyticsFallbacks.Add(1, new KeyValuePair<string, object?>("channel", channel));

    public static void RedirectAnalyticsUnconfirmed(string channel) =>
        RedirectAnalyticsUnconfirmedEvents.Add(1, new KeyValuePair<string, object?>("channel", channel));
}
