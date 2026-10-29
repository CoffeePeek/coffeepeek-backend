namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>Shops → Media: reserve the caller's shelf-bag upload for one bag.</summary>
public sealed record ClaimShelfBagPhotoRequest(Guid BagId, string StorageKey);
