namespace CoffeePeek.Contract.Events;

/// <summary>Shops removed one photo from a live check-in (a hidden shelf-bag card); Media removes its bytes.</summary>
public sealed record CheckInPhotoDetachedEvent(Guid UserId, Guid CheckInId, Guid PhotoId, DateTime DetachedAtUtc);
