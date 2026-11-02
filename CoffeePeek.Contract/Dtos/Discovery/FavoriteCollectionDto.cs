namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class FavoriteCollectionDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string CanonicalPath { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required FavoriteCollectionPlace[] Items { get; init; }
}
