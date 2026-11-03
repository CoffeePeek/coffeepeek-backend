namespace CoffeePeek.Contract.Events;

/// <summary>Emitted through the Discovery transaction outbox after a cover becomes current.</summary>
public sealed record CollectionCoverAttachedEvent(Guid UserId, Guid CollectionId, Guid PhotoId);
