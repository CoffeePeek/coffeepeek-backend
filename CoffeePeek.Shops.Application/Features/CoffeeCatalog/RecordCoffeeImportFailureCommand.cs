using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record RecordCoffeeImportFailureCommand(CoffeeImportFailureDto Failure);
