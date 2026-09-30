using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

/// <summary>Append-only chronology: unlike alias rows, A→B→A retains both transitions.</summary>
public sealed class PublicAddressAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PublicAddressKind Kind { get; init; }
    public Guid OwnerId { get; init; }
    public long Revision { get; init; }
    public string? PreviousSlug { get; init; }
    public string? Slug { get; init; }
    public string Action { get; init; } = null!;
    public Guid? ActorId { get; init; }
    public string Reason { get; init; } = null!;
    public DateTime AtUtc { get; init; }
}
