using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeImportFailureDto(string SourceKey, string SnapshotId, DateTimeOffset CollectedAtUtc,
    string Stage, string Error);
