namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

public sealed record SavedCollectionCover(Guid PhotoId, string StorageKey, string ContentType);
