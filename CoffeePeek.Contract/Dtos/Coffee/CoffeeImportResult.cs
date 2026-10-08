using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeImportResult(Guid RunId, string SourceKey, string SnapshotId,
    string Status, int Products, int Variants, int Added, bool MissingAvailabilityApplied, DateTime AppliedAtUtc);
