using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>Lets two profile requests read the same revision before either handler can write.</summary>
public sealed class ShopProfileReadBarrierInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int shopReads;

    public int ShopReads => Volatile.Read(ref shopReads);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM \"Shops\"", StringComparison.Ordinal))
        {
            var ordinal = Interlocked.Increment(ref shopReads);
            if (ordinal == 2)
                bothRead.TrySetResult();
            if (ordinal <= 2)
                await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }

        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}
