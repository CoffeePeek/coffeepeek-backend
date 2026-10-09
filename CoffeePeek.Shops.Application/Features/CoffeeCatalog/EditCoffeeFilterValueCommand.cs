using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record EditCoffeeFilterValueCommand(Guid Id, EditCoffeeFilterValueRequest Request);
