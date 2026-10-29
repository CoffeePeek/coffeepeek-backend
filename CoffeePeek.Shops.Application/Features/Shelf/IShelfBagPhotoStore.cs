namespace CoffeePeek.Shops.Application.Features.Shelf;

/// <summary>Shelf → Media port. Media owns the bytes; Shelf owns which bag shows them.</summary>
public interface IShelfBagPhotoStore
{
    /// <summary>Reserves the caller's validated upload for one bag. Retrying the same bag and key is idempotent.</summary>
    Task<ShelfBagPhotoClaimResult> ClaimAsync(Guid userId, Guid bagId, string storageKey, CancellationToken ct);
}
