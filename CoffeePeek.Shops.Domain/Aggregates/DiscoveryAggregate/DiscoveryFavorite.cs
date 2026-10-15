using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

public sealed class DiscoveryFavorite
{
    public Guid UserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private DiscoveryFavorite() { }
    public DiscoveryFavorite(Guid userId, string kind, Guid entityId)
    {
        if (userId == Guid.Empty || entityId == Guid.Empty || kind is not ("coffee_shop" or "roaster" or "drink"))
            throw new DomainException("Invalid discovery favorite.");
        UserId = userId; Kind = kind; EntityId = entityId; CreatedAtUtc = DateTime.UtcNow;
    }
}
