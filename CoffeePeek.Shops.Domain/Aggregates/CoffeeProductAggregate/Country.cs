using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class Country
{
    public string Code { get; private set; } = null!;
    public string NameRu { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    private Country() { }
    public Country(string code, string nameRu, string nameEn) { Code = code; NameRu = nameRu; NameEn = nameEn; }
}
