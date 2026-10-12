using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record EditCoffeeVariantClassificationCommand(Guid Id, Guid VariantId, VariantClassificationEditRequest Request);
