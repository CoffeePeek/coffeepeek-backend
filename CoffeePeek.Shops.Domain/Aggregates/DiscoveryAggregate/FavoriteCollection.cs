using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

/// <summary>Explicitly shared membership; changing favorites never changes this collection.</summary>
public sealed class FavoriteCollection
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public List<FavoriteCollectionItem> Items { get; private set; } = [];
    private FavoriteCollection() { }

    public FavoriteCollection(Guid ownerId, string title, IEnumerable<DiscoveryFavorite> favorites)
    {
        var selected = favorites.ToArray();
        title = title.Trim();
        if (ownerId == Guid.Empty || title.Length is < 1 or > 100 || title.Any(char.IsControl) ||
            selected.Length is < 1 or > 100 || selected.Any(f => f.UserId != ownerId || f.Kind is not ("coffee_shop" or "roaster")) ||
            selected.Select(f => (f.Kind, f.EntityId)).Distinct().Count() != selected.Length)
            throw new DomainException("Invalid favorite collection.");
        Id = Guid.NewGuid(); OwnerId = ownerId; Title = title; CreatedAtUtc = DateTime.UtcNow;
        Items = selected.Select((favorite, position) => new FavoriteCollectionItem(Id, position, favorite.Kind, favorite.EntityId)).ToList();
    }

    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;
}
