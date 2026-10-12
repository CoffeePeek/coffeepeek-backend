using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeImportRun : Entity<Guid>
{
    public string SourceKey { get; private set; } = null!;
    public string SnapshotId { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public DateTime CollectedAtUtc { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }
    public string Status { get; private set; } = "Pending";
    public int Products { get; private set; }
    public int Variants { get; private set; }
    public int Added { get; private set; }
    public bool MissingAvailabilityApplied { get; private set; }
    public string? Error { get; private set; }
    private CoffeeImportRun() { }
    public CoffeeImportRun(string sourceKey, string snapshotId, string payloadHash)
    {
        CoffeeProductSource.ValidateIdentity(sourceKey, 255);
        ValidateHash(snapshotId); ValidateHash(payloadHash);
        Id = Guid.NewGuid(); SourceKey = sourceKey; SnapshotId = snapshotId; PayloadHash = payloadHash;
    }

    public void MarkFailed(DateTime collectedAtUtc, string error)
    {
        ValidateTime(collectedAtUtc);
        if (Status == "Applied" || string.IsNullOrWhiteSpace(error) || error.Length > 2000)
            throw new DomainException("Invalid import failure transition.");
        CollectedAtUtc = collectedAtUtc; AppliedAtUtc = DateTime.UtcNow; Status = "Failed"; Error = error;
    }

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
        ValidateHash(payloadHash); ValidateTime(collectedAtUtc);
        if (products < 0 || variants < 0 || added < 0 || added > products)
            throw new DomainException("Invalid import counters.");
        if (!CanApplyPayload(payloadHash))
            throw new InvalidOperationException("Snapshot identity was reused for different content.");
        if (Status == "Applied") return;
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

    private static void ValidateHash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new DomainException("Invalid import payload identity.");
    }

    private static void ValidateTime(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new DomainException("Import observation must have a UTC timestamp.");
    }
}
