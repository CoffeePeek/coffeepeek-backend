using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed class CoffeeSnapshotOrigin
{
    public string CountryCode { get; set; } = string.Empty;
    public string? EvidenceField { get; set; }
}
