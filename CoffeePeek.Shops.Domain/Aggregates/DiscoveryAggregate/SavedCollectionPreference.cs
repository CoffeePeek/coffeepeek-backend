namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

/// <summary>Discovery owns the optional single collection displayed on a profile.</summary>
public sealed class SavedCollectionPreference
{
    public Guid OwnerId { get; private set; }
    public Guid? CollectionId { get; private set; }
    private SavedCollectionPreference() { }
    public SavedCollectionPreference(Guid ownerId) => OwnerId = ownerId;
    public void Select(Guid? collectionId) => CollectionId = collectionId;
}
