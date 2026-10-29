namespace CoffeePeek.Contract.Dtos.Shelf;

/// <param name="StorageKey">Immutable private object key; never exposed to clients.</param>
public sealed record ClaimedShelfBagPhotoDto(Guid PhotoId, string StorageKey, string ContentType, long SizeBytes);
