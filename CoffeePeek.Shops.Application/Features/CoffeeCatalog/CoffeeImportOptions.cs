using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class CoffeeImportOptions
{
    // Key is an exact source hostname; value maps an exact source brand to a pre-existing RoasterId.
    public Dictionary<string, Dictionary<string, Guid>> Roasters { get; set; } = new(StringComparer.Ordinal);
}
