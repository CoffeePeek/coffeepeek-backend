namespace CoffeePeek.Shops.Persistance.Discovery;

internal sealed record CollectionProjectionSource(Guid Id, string Title, DateTime CreatedAtUtc, string Mode,
    string Visibility, string? Description, Guid? CoverPhotoId, long Revision, DateTime UpdatedAtUtc);
