using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed class CoffeeImportStorage(ShopsDbContext db) : ICoffeeImportStorage
{
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        Func<Task<T>> transactional = () => PublicAddressTransaction.RunAsync(db, operation, ct);
        try
        {
            return db.Database.CurrentTransaction is null
                ? await db.Database.CreateExecutionStrategy().ExecuteAsync(transactional)
                : await transactional();
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    public async Task<CoffeeImportState> LoadAsync(string sourceKey, string snapshotId, Guid[] roasterIds, CancellationToken ct) => new(
        await db.CoffeeImportRuns.SingleOrDefaultAsync(r => r.SourceKey == sourceKey && r.SnapshotId == snapshotId, ct),
        await db.CoffeeImportRuns.Where(r => r.SourceKey == sourceKey && r.Status == "Applied")
            .OrderByDescending(r => r.CollectedAtUtc).FirstOrDefaultAsync(ct),
        await db.Roasters.Where(r => roasterIds.Contains(r.Id)).Select(r => r.Id).ToArrayAsync(ct),
        await db.Countries.Select(c => c.Code).ToArrayAsync(ct),
        await db.CoffeeFilterValues.AsNoTracking().Where(v => v.IsActive).ToArrayAsync(ct),
        await db.CoffeeProductSources.Where(s => s.SourceKey == sourceKey)
            .Include(s => s.Product).ThenInclude(p => p.Countries)
            .Include(s => s.Variants).Include(s => s.Photos).AsSplitQuery().ToListAsync(ct));

    public async Task SaveAsync(CoffeeImportChanges changes, CancellationToken ct)
    {
        db.CoffeeProducts.AddRange(changes.NewProducts);
        db.Set<CoffeeVariant>().AddRange(changes.NewVariants);
        db.Set<CoffeeProductPhoto>().AddRange(changes.NewPhotos);
        db.Set<CoffeeProductPhoto>().RemoveRange(changes.RemovedPhotos);
        if (changes.AddRun) db.CoffeeImportRuns.Add(changes.Run);
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(CoffeeImportRun run, CancellationToken ct)
    {
        if (await db.CoffeeImportRuns.AnyAsync(r => r.SourceKey == run.SourceKey && r.SnapshotId == run.SnapshotId, ct)) return;
        db.CoffeeImportRuns.Add(run);
        try
        {
            // Flush identifies concurrent duplicate reports before the enclosing Wolverine commit.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_CoffeeImportRuns_SourceKey_SnapshotId" })
        { db.Entry(run).State = EntityState.Detached; }
    }
}
