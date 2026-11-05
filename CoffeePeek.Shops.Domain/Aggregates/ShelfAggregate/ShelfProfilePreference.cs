namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>Controls the current shelf's public profile visibility; visible by default.</summary>
public sealed class ShelfProfilePreference
{
    public Guid OwnerId { get; private set; }
    public bool IsVisible { get; private set; } = true;
    private ShelfProfilePreference() { }
    public ShelfProfilePreference(Guid ownerId) => OwnerId = ownerId;
    public void SetVisibility(bool isVisible) => IsVisible = isVisible;
}
