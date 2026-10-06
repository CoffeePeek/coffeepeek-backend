using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeImportRun : Entity<Guid>
{
    public string SourceKey { get; set; } = null!;
    public string SnapshotId { get; set; } = null!;
    public string PayloadHash { get; set; } = null!;
    public DateTime CollectedAtUtc { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public string Status { get; set; } = "Applied";
    public int Products { get; set; }
    public int Variants { get; set; }
    public int Added { get; set; }
    public bool MissingAvailabilityApplied { get; set; }
    public string? Error { get; set; }
    public CoffeeImportRun() { Id = Guid.NewGuid(); }
}
