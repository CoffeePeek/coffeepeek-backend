using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

// Wire contract for the existing Python snapshots; unrelated/raw source fields are retained in SourceProperties.
public sealed class CoffeeSnapshotImportRequest
{
    public string SourceKey { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public CoffeeSnapshotDto Snapshot { get; set; } = new();
}
