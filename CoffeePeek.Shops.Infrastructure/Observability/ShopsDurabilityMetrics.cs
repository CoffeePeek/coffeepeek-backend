using System.Diagnostics.Metrics;
using CoffeePeek.Shops.Application.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;

namespace CoffeePeek.Shops.Infrastructure.Observability;

public sealed class ShopsDurabilityMetrics : BackgroundService
{
    private readonly IMessageStore store;
    private readonly ILogger<ShopsDurabilityMetrics> logger;
    private readonly Meter meter = new(ShopsTelemetry.InstrumentationName);
    private Snapshot? snapshot;

    public ShopsDurabilityMetrics(IMessageStore store, ILogger<ShopsDurabilityMetrics> logger)
    {
        this.store = store; this.logger = logger;
        meter.CreateObservableGauge("shops.durability.dead_letters", () => Read(s => s.Counts.DeadLetter),
            unit: "messages", description: "Provider-reported dead letter count; PostgreSQL may use table estimates.");
        // shortcut: 6.35 has no durable enqueue timestamp; use continuous observed backlog age until a schema-supported age exists.
        meter.CreateObservableGauge("shops.durability.outbox.observed_age", () => Read(s => s.BacklogSince is { } since
            ? Math.Max(0, (DateTimeOffset.UtcNow - since).TotalSeconds) : 0), unit: "s",
            description: "Age of provider-reported backlog across successful 30s samples; resets on restart or an empty sample. Read with sample_age.");
        meter.CreateObservableGauge("shops.durability.sample_age", () => Read(s =>
            Math.Max(0, (DateTimeOffset.UtcNow - s.SampledAt).TotalSeconds)), unit: "s",
            description: "Age of the last successful storage sample; a failed poll retains the prior sample.");
    }

    private IEnumerable<Measurement<double>> Read(Func<Snapshot, double> value)
    {
        var current = Volatile.Read(ref snapshot);
        return current is null ? [] : [new(value(current))];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var counts = await store.Admin.FetchCountsAsync();
                var now = DateTimeOffset.UtcNow;
                var since = counts.Outgoing > 0 ? snapshot?.BacklogSince ?? now : (DateTimeOffset?)null;
                Volatile.Write(ref snapshot, new(counts, now, since));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not sample Shops durable message storage");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override void Dispose() { meter.Dispose(); base.Dispose(); }
    private sealed record Snapshot(PersistedCounts Counts, DateTimeOffset SampledAt, DateTimeOffset? BacklogSince);
}
