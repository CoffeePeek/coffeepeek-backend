namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

public sealed class FavoriteCollectionItem
{
    public Guid CollectionId { get; private set; }
    public int Position { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    private FavoriteCollectionItem() { }
    internal FavoriteCollectionItem(Guid collectionId, int position, string kind, Guid entityId)
    { CollectionId = collectionId; Position = position; Kind = kind; EntityId = entityId; }
}
