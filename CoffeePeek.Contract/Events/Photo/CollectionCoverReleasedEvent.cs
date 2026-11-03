namespace CoffeePeek.Contract.Events;

public sealed record CollectionCoverReleasedEvent(Guid UserId, Guid CollectionId, Guid PhotoId, DateTime ReleasedAtUtc);
