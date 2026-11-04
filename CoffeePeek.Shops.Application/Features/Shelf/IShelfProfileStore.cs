using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf;

public interface IShelfProfileStore
{
    Task<ShelfProfileVisibilityDto> VisibilityAsync(Guid owner, CancellationToken ct);
    Task<ShelfProfileVisibilityDto> SetVisibilityAsync(Guid owner, bool isVisible, CancellationToken ct);
    Task<PublicShelfDto?> PublicAsync(Guid owner, string profileSlug, CancellationToken ct);
    Task<ShelfBagPhotoAccess?> PublicPhotoAsync(Guid owner, Guid bagId, Guid photoId, CancellationToken ct);
    Task DeleteForOwnerAsync(Guid owner, CancellationToken ct);
}
