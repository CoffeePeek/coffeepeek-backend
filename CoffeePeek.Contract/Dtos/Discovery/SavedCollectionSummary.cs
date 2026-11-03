namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed record SavedCollectionSummary(Guid Id, string Title, string? Description, string Visibility,
    SavedCollectionCoverDto? CoverPhoto, int ItemCount, long Revision, bool ContainsItem, bool IsFeatured,
    string? CanonicalPath)
{
    public string Mode => "list";
}
