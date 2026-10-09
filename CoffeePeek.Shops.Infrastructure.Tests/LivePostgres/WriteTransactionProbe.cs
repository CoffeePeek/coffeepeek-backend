using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class WriteTransactionProbe : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, long> started = new();
    public ConcurrentBag<double> CommittedMilliseconds { get; } = [];

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Assert.True(started.TryAdd(eventData.TransactionId, Stopwatch.GetTimestamp()));
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Assert.True(started.TryRemove(eventData.TransactionId, out var timestamp));
        CommittedMilliseconds.Add(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        started.TryRemove(eventData.TransactionId, out _);
        return Task.CompletedTask;
    }
}
