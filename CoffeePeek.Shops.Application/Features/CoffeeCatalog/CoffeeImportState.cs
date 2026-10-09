using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record CoffeeImportState(CoffeeImportRun? PreviousRun, CoffeeImportRun? LastAppliedRun,
    Guid[] ExistingRoasterIds, string[] CountryCodes, CoffeeFilterValue[] ActiveClassification,
    List<CoffeeProductSource> Sources);
