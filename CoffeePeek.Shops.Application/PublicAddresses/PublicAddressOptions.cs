using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed class PublicAddressOptions
{
    // Enable only after migrations and the live PostgreSQL transaction gate.
    public bool Enabled { get; set; }
}
