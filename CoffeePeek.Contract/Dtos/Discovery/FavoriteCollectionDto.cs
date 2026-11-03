namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class FavoriteCollectionDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string CanonicalPath { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required FavoriteCollectionPlace[] Items { get; init; }
    public int ItemCount => Items.Length;
    public string Mode { get; init; } = "snapshot";
    public string Visibility { get; init; } = "public";
    public string? Description { get; init; }
    public SavedCollectionCoverDto? CoverPhoto { get; init; }
    public long Revision { get; init; } = 1;
    public DateTime UpdatedAtUtc { get; init; }
}
