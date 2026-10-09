using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record CoffeeImportChanges(IReadOnlyCollection<CoffeeProduct> NewProducts,
    IReadOnlyCollection<CoffeeVariant> NewVariants, IReadOnlyCollection<CoffeeProductPhoto> NewPhotos,
    IReadOnlyCollection<CoffeeProductPhoto> RemovedPhotos, CoffeeImportRun Run, bool AddRun);
