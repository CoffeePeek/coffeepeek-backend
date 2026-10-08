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

    /// <summary>
    /// A pre-apply failure report has no payload, so its snapshot ID is only a
    /// placeholder hash until the first complete payload is supplied.
    /// </summary>
    public bool CanApplyPayload(string payloadHash) => PayloadHash == payloadHash ||
        (Status == "Failed" && PayloadHash == SnapshotId && Error is not null &&
         (Error.StartsWith("collect: ", StringComparison.Ordinal) ||
          Error.StartsWith("photos: ", StringComparison.Ordinal) ||
          Error.StartsWith("apply: ", StringComparison.Ordinal)));

    public void MarkApplied(string payloadHash, DateTime collectedAtUtc, int products, int variants,
        int added, bool missingAvailabilityApplied)
    {
        if (!CanApplyPayload(payloadHash))
            throw new InvalidOperationException("Snapshot identity was reused for different content.");
        PayloadHash = payloadHash;
        CollectedAtUtc = collectedAtUtc;
        AppliedAtUtc = DateTime.UtcNow;
        Status = "Applied";
        Error = null;
        Products = products;
        Variants = variants;
        Added = added;
        MissingAvailabilityApplied = missingAvailabilityApplied;
    }
}
