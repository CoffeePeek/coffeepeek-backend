namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

/// <summary>A replaced upload cannot be reused while its durable release is still in flight.</summary>
public sealed class SavedCollectionCoverUse
{
    public Guid PhotoId { get; private set; }
    public Guid CollectionId { get; private set; }
    private SavedCollectionCoverUse() { }
    public SavedCollectionCoverUse(Guid photoId, Guid collectionId) { PhotoId = photoId; CollectionId = collectionId; }
}
