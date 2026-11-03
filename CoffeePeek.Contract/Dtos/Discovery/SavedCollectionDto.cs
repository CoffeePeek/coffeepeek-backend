namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed record SavedCollectionDto(Guid Id, string Title, string? Description, string Visibility,
    SavedCollectionCoverDto? CoverPhoto, int ItemCount, long Revision, bool IsFeatured, string? CanonicalPath,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, FavoriteCollectionPlace[] Items)
{
    public string Mode => "list";
}
