using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed partial class CoffeeCatalogStore
{
    public Task RecordFailureAsync(CoffeeImportFailureDto failure, CancellationToken ct) =>
        ImportService().RecordFailureAsync(failure, ct);
    public Task<CoffeeImportResult> ImportAsync(CoffeeSnapshotImportRequest request, CancellationToken ct) =>
        ImportService().ImportAsync(request, ct);
    private CoffeeImportService ImportService() => new(new CoffeeImportStorage(db), importOptions, logger);
}
