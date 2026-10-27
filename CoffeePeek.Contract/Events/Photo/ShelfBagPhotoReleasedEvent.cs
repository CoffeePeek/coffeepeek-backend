namespace CoffeePeek.Contract.Events;

/// <summary>Shops replaced or deleted a bag's card; Media makes its bytes unreadable and removes them.</summary>
public sealed record ShelfBagPhotoReleasedEvent(Guid UserId, Guid BagId, Guid PhotoId, DateTime ReleasedAtUtc);
