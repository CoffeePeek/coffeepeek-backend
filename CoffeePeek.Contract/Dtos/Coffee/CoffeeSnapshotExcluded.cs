using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed class CoffeeSnapshotExcluded
{
    public string? SourceProductId { get; set; }
    public string? Reason { get; set; }
    public CoffeeSnapshotVariant[] Variants { get; set; } = [];
}
