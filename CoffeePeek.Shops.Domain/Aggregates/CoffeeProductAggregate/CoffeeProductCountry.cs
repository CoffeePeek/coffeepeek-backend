using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductCountry
{
    public Guid CoffeeProductId { get; private set; }
    public string CountryCode { get; private set; } = null!;
    public Country Country { get; private set; } = null!;
    private CoffeeProductCountry() { }
    public CoffeeProductCountry(Guid productId, string code) { CoffeeProductId = productId; CountryCode = code; }
}
