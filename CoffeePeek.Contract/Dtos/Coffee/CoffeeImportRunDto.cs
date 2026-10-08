using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeImportRunDto(Guid Id, string SourceKey, string SnapshotId, string Status,
    DateTime CollectedAtUtc, DateTime AppliedAtUtc, int Products, int Variants, int Added,
    bool MissingAvailabilityApplied, string? Error);
