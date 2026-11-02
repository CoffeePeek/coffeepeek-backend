namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed record FavoriteCollectionSummary(Guid Id, string Title, string CanonicalPath, DateTime CreatedAtUtc, int ItemCount);
