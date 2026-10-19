using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Observability;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeePeek.Shops.Infrastructure.Account;

public sealed class CheckInPhotoVisibilityCache(TimeProvider clock)
    : ICheckInPhotoVisibilityCache, IDisposable
{
    // Dedicated bounded cache: do not impose size accounting on unrelated application caches.
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 4 * 1024 * 1024 });
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task<CheckInPhotoVisibilityFacts> GetAsync(Guid? viewerId, IAuthorVisibilityLookup source, CancellationToken ct)
    {
        if (viewerId is null) return new([], CheckInPhotoPolicyObservation.Start(clock));
        using var measurement = PhotoDeliveryTelemetry.Start("policy_cache", "authenticated");
        var gate = gates[(uint)viewerId.Value.GetHashCode() % gates.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<CheckInPhotoVisibilityFacts>(viewerId.Value, out var found)
                && found!.Observation.Age(clock) < CheckInPhotoPolicyObservation.FactsLifetime)
            { measurement.Complete("hit"); return found; }
            cache.Remove(viewerId.Value);
            var facts = await ReadAsync(viewerId.Value, source, ct);
            var remaining = CheckInPhotoPolicyObservation.FactsLifetime - facts.Observation.Age(clock);
            if (remaining <= TimeSpan.Zero) throw new ServiceUnavailableException("Photo visibility observation expired.");
            // Only the current caller performed this read; later callers receive cached provenance.
            cache.Set(viewerId.Value, facts with { ReadFromSource = false }, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = remaining,
                Size = 128L + facts.ExcludedUserIds.Length * 16L
            });
            measurement.Complete("miss");
            return facts;
        }
        finally { gate.Release(); }
    }

    private async Task<CheckInPhotoVisibilityFacts> ReadAsync(Guid viewerId, IAuthorVisibilityLookup source, CancellationToken ct)
    {
        // Include all Account network/retry time in the original deadline.
        var observation = CheckInPhotoPolicyObservation.Start(clock);
        return new(await source.ExcludedUserIdsAsync(viewerId, ct), observation) { ReadFromSource = true };
    }

    public void Dispose()
    {
        cache.Dispose();
        foreach (var gate in gates) gate.Dispose();
    }
}
