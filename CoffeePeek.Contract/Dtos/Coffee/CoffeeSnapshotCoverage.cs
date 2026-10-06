using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed class CoffeeSnapshotCoverage
{
    public int? ExpectedProducts { get; set; }
    public int? DiscoveredProducts { get; set; }
}
