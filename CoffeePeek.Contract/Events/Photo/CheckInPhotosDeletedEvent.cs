namespace CoffeePeek.Contract.Events;

public sealed record CheckInPhotosDeletedEvent(Guid CheckInId, Guid UserId, DateTime DeletedAtUtc);
