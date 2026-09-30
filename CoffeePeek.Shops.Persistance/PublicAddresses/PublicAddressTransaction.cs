using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

internal static class PublicAddressTransaction
{
    public static async Task<T> RunAsync<T>(ShopsDbContext db, Func<Task<T>> operation, CancellationToken ct)
    {
        // InMemory is only a behavior-test substitute; concurrency is verified separately against PostgreSQL.
        if (!db.Database.IsRelational()) return await operation();
        var ambient = db.Database.CurrentTransaction;
        await using var owned = ambient is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var transaction = ambient ?? owned!;
        var savepoint = "slug_" + Guid.NewGuid().ToString("N");
        if (ambient is not null) await transaction.CreateSavepointAsync(savepoint, ct);
        try
        {
            // One lock for this initial rollout serializes both typed namespaces and owner deletion.
            // Transaction scope (including Wolverine's ambient transaction), fixed key, no hash collisions.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7402100101)", ct);
            var result = await operation();
            if (owned is not null) await owned.CommitAsync(ct);
            else await transaction.ReleaseSavepointAsync(savepoint, ct);
            return result;
        }
        catch
        {
            try
            {
                if (owned is not null) await owned.RollbackAsync(CancellationToken.None);
                else await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            finally
            {
                // Never retry on stale accepted EF state after a rollback. Caller must restart the operation.
                db.ChangeTracker.Clear();
            }
            throw;
        }
    }
}
