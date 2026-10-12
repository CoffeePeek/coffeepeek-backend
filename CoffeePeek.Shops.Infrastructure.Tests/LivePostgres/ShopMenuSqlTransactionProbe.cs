using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class ShopMenuSqlTransactionProbe : DbCommandInterceptor
{
    private int sawUpsert;
    private int upsertHadTransaction;

    public bool SawUpsert => Volatile.Read(ref sawUpsert) != 0;
    public bool UpsertHadTransaction => Volatile.Read(ref upsertHadTransaction) != 0;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("INSERT INTO \"ShopMenuItems\"", StringComparison.Ordinal))
        {
            Interlocked.Exchange(ref sawUpsert, 1);
            Interlocked.Exchange(ref upsertHadTransaction, command.Transaction is null ? 0 : 1);
        }

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
