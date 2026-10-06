using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed class CoffeeSnapshotDto
{
    public int SchemaVersion { get; set; }
    public string AdapterVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CollectedAtUtc { get; set; }
    public bool Complete { get; set; }
    public CoffeeSnapshotProduct[] Products { get; set; } = [];
    public CoffeeSnapshotCoverage[] Coverage { get; set; } = [];
    public CoffeeSnapshotExcluded[] Excluded { get; set; } = [];
}
