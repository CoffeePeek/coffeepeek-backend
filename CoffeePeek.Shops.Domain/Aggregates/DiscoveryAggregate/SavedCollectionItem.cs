namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

public sealed class SavedCollectionItem
{
    public Guid CollectionId { get; private set; }
    public int Position { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    private SavedCollectionItem() { }
    internal SavedCollectionItem(Guid collectionId, int position, string kind, Guid entityId)
    { CollectionId = collectionId; Position = position; Kind = kind; EntityId = entityId; }
    internal void Move(int position) => Position = position;
}
