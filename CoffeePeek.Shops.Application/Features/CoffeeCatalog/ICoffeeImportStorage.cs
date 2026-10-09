using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeImportStorage
{
    Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct);
    Task<CoffeeImportState> LoadAsync(string sourceKey, string snapshotId, Guid[] roasterIds, CancellationToken ct);
    Task SaveAsync(CoffeeImportChanges changes, CancellationToken ct);
    Task RecordFailureAsync(CoffeeImportRun run, CancellationToken ct);
}
