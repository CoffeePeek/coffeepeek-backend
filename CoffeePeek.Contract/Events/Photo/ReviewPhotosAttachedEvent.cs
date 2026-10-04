namespace CoffeePeek.Contract.Events;

/// <summary>Durable confirmation of newly attached review uploads, emitted in the moderation outbox.</summary>
public record ReviewPhotosAttachedEvent(
    Guid UserId,
    Guid ReviewId,
    IReadOnlyCollection<string> StorageKeys,
    DateTime AttachedAt);
