namespace CoffeePeek.Contract.Dtos.Discovery;

/// <summary>Trusted internal metadata. StorageKey is never exposed in collection read DTOs.</summary>
public sealed record ClaimedCollectionCoverDto(Guid PhotoId, string StorageKey, string ContentType, long SizeBytes);
