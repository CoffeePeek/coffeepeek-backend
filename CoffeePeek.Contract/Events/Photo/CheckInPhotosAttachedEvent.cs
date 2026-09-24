namespace CoffeePeek.Contract.Events;

/// <summary>
/// Published by Shops when a check-in with photos is committed. MediaService marks the photos
/// permanent in storage and Confirmed in metadata so the same keys cannot be attached again.
/// </summary>
public record CheckInPhotosAttachedEvent(
    Guid UserId,
    Guid CheckInId,
    IReadOnlyCollection<string> StorageKeys,
    DateTime AttachedAt
);
