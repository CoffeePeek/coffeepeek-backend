using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeImportWriter
{
    Task<CoffeeImportResult> ImportAsync(CoffeeSnapshotImportRequest request, CancellationToken ct);
    Task RecordFailureAsync(CoffeeImportFailureDto failure, CancellationToken ct);
}
