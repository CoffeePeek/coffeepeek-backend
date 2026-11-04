namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>Explicit opt-in to displaying the current shelf on the owner's public profile.</summary>
public sealed class ShelfProfilePreference
{
    public Guid OwnerId { get; private set; }
    public bool IsVisible { get; private set; }
    private ShelfProfilePreference() { }
    public ShelfProfilePreference(Guid ownerId) => OwnerId = ownerId;
    public void SetVisibility(bool isVisible) => IsVisible = isVisible;
}
