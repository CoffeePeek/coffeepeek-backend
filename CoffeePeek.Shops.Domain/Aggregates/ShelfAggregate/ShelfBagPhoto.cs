namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>Media-trusted card image. StorageKey is the private immutable copy, never shown to clients.</summary>
public sealed record ShelfBagPhoto(Guid Id, string StorageKey, string ContentType);
