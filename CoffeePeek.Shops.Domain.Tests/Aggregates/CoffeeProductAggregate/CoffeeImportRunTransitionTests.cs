using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeImportRunTransitionTests
{
    [Fact]
    public void PreApplyFailureAcceptsFirstPayloadAndAppliedReplayKeepsOriginalCounters()
    {
        var run = new CoffeeImportRun("test.example", new('a', 64), new('a', 64));
        run.MarkFailed(DateTime.UtcNow, "photos: interrupted");
        Assert.True(run.CanApplyPayload(new('b', 64)));
        run.MarkApplied(new('b', 64), DateTime.UtcNow, 2, 3, 1, true);
        var appliedAt = run.AppliedAtUtc;
        run.MarkApplied(new('b', 64), DateTime.UtcNow, 4, 5, 2, false);
        Assert.Equal("Applied", run.Status); Assert.Null(run.Error);
        Assert.Equal(2, run.Products); Assert.Equal(3, run.Variants); Assert.Equal(1, run.Added);
        Assert.True(run.MissingAvailabilityApplied); Assert.Equal(appliedAt, run.AppliedAtUtc);
    }

    [Fact]
    public void FailedPayloadCannotBeReplacedUnderSameSnapshotIdentity()
    {
        var run = new CoffeeImportRun("test.example", new('a', 64), new('b', 64));
        run.MarkFailed(DateTime.UtcNow, "Database unavailable");
        Assert.False(run.CanApplyPayload(new('c', 64)));
        Assert.Throws<InvalidOperationException>(() => run.MarkApplied(new('c', 64), DateTime.UtcNow, 1, 1, 1, false));
        Assert.Equal("Failed", run.Status); Assert.Equal(new('b', 64), run.PayloadHash);
    }

    [Fact]
    public void AppliedRunCannotBecomeFailed()
    {
        var run = new CoffeeImportRun("test.example", new('a', 64), new('b', 64));
        run.MarkApplied(new('b', 64), DateTime.UtcNow, 1, 1, 1, false);
        Assert.Throws<DomainException>(() => run.MarkFailed(DateTime.UtcNow, "Late error"));
        Assert.Equal("Applied", run.Status); Assert.Null(run.Error);
    }

    [Fact]
    public void InvalidCountersDoNotApplyRun()
    {
        var run = new CoffeeImportRun("test.example", new('a', 64), new('b', 64));
        Assert.Throws<DomainException>(() => run.MarkApplied(new('b', 64), DateTime.UtcNow, 1, 1, 2, false));
        Assert.Equal("Pending", run.Status); Assert.Equal(0, run.Products); Assert.Equal(default, run.AppliedAtUtc);
    }
}
