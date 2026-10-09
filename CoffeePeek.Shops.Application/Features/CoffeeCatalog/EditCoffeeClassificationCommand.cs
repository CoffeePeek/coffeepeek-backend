using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record EditCoffeeClassificationCommand(Guid Id, CoffeeClassificationEditRequest Request);
