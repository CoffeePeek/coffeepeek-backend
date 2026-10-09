using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class ReadPerformanceCommandProbe(bool capturePlans = false) : DbCommandInterceptor, IDisposable
{
    public int Count { get; private set; }
    public List<NpgsqlCommand> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count++;
        if (capturePlans)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Performance plan capture accepts only generated SELECT commands.");
            var copy = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.CommandText);
            foreach (NpgsqlParameter parameter in command.Parameters)
                copy.Parameters.Add((NpgsqlParameter)((ICloneable)parameter).Clone());
            Commands.Add(copy);
        }
        return ValueTask.FromResult(result);
    }

    public void Dispose()
    {
        foreach (var command in Commands) command.Dispose();
    }
}
