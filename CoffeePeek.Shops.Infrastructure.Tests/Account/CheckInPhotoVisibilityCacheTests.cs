using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Account;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Account;

public sealed class CheckInPhotoVisibilityCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RepeatedHitsKeepOriginAndRefreshAtExactly20SecondsPerViewer()
    {
        var clock = new Clock(); var source = new Source(); var viewer = Guid.NewGuid(); var blocked = Guid.NewGuid();
        using var cache = Cache(clock);
        var original = await cache.GetAsync(viewer, source, Ct);
        clock.Advance(19); source.Excluded = [blocked];
        var hit = await cache.GetAsync(viewer, source, Ct);
        Assert.True(original.ReadFromSource);
        Assert.False(hit.ReadFromSource);
        Assert.Equal(original.Observation, hit.Observation);
        Assert.Empty(hit.ExcludedUserIds);
        Assert.Equal(TimeSpan.FromSeconds(21), hit.Observation.Remaining(clock));
        Assert.Equal(new[] { blocked }, (await cache.GetAsync(Guid.NewGuid(), source, Ct)).ExcludedUserIds);
        clock.Advance(1);
        var fresh = await cache.GetAsync(viewer, source, Ct);
        Assert.True(fresh.ReadFromSource);
        Assert.Equal(new[] { blocked }, fresh.ExcludedUserIds);
        Assert.NotEqual(original.Observation, fresh.Observation);
        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task ExpiredFactsAreNeverUsedDuringAccountOutageOrCancellation()
    {
        var clock = new Clock(); var source = new Source(); var viewer = Guid.NewGuid();
        using var cache = Cache(clock);
        await cache.GetAsync(viewer, source, Ct);
        source.Fail = true; clock.Advance(20);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => cache.GetAsync(viewer, source, Ct));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => cache.GetAsync(viewer, source, Ct));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(viewer, source, cancelled.Token));
        source.Fail = false;
        Assert.Empty((await cache.GetAsync(viewer, source, Ct)).ExcludedUserIds);
        Assert.Equal(4, source.Calls);
    }

    [Fact]
    public async Task SlowLookupAndWallClockRollbackCannotRenewFacts()
    {
        var clock = new Clock(); var source = new Source { OnRead = () => clock.Advance(20) }; var viewer = Guid.NewGuid();
        using var cache = Cache(clock);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => cache.GetAsync(viewer, source, Ct));
        source.OnRead = null;
        var origin = await cache.GetAsync(viewer, source, Ct);
        clock.Advance(21); clock.WallOffset = -120;
        Assert.NotSame(origin, await cache.GetAsync(viewer, source, Ct));
        Assert.Equal(TimeSpan.FromSeconds(19), origin.Observation.Remaining(clock));
        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneLookupWithoutSlidingItsDeadline()
    {
        var clock = new Clock(); var started = new TaskCompletionSource(); var complete = new TaskCompletionSource();
        var source = new Source { Wait = async () => { started.TrySetResult(); await complete.Task; } };
        using var cache = Cache(clock);
        var viewer = Guid.NewGuid();
        var requests = Enumerable.Range(0, 20).Select(_ => cache.GetAsync(viewer, source, Ct)).ToArray();
        await started.Task; clock.Advance(3); complete.SetResult();
        var facts = await Task.WhenAll(requests);
        Assert.Equal(1, source.Calls);
        Assert.Single(facts, f => f.ReadFromSource);
        Assert.All(facts, f => Assert.Equal(facts[0].Observation, f.Observation));
        Assert.All(facts, f => Assert.Empty(f.ExcludedUserIds));
        Assert.Equal(TimeSpan.FromSeconds(37), facts[0].Observation.Remaining(clock));
    }

    [Fact]
    public async Task RollbackFlagBypassesFactsAndAnonymousNeverCallsAccount()
    {
        var clock = new Clock(); var source = new Source(); var viewer = Guid.NewGuid();
        var options = new CheckInPhotoDeliveryOptions { CachePublicVariants = true };
        using var cache = new CheckInPhotoVisibilityCache(clock, Options.Create(options));
        await cache.GetAsync(viewer, source, Ct);
        options.CachePublicVariants = false; source.Excluded = [Guid.NewGuid()];
        var first = await cache.GetAsync(viewer, source, Ct);
        var second = await cache.GetAsync(viewer, source, Ct);
        Assert.Single(first.ExcludedUserIds);
        Assert.Single(second.ExcludedUserIds);
        Assert.True(first.ReadFromSource);
        Assert.True(second.ReadFromSource);
        Assert.Empty((await cache.GetAsync(null, source, Ct)).ExcludedUserIds);
        Assert.Equal(3, source.Calls);
    }

    private static CheckInPhotoVisibilityCache Cache(Clock clock) => new(clock,
        Options.Create(new CheckInPhotoDeliveryOptions { CachePublicVariants = true }));

    private sealed class Clock : TimeProvider
    {
        private long seconds;
        public int WallOffset;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(seconds + WallOffset);
        public void Advance(int value) => seconds += value;
    }

    private sealed class Source : IAuthorVisibilityLookup
    {
        public int Calls;
        public Guid[] Excluded = [];
        public bool Fail;
        public Action? OnRead;
        public Func<Task>? Wait;
        public async Task<Guid[]> ExcludedUserIdsAsync(Guid? viewerId, CancellationToken ct)
        {
            Calls++; OnRead?.Invoke();
            if (Wait is not null) await Wait();
            if (Fail) throw new ServiceUnavailableException("Account unavailable");
            return Excluded;
        }
    }
}
