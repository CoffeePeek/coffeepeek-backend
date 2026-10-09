using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.Shops.Application.Abstractions;

/// <summary>
/// Resolves storage keys to trusted metadata for photos the user uploaded through MediaService
/// and has not attached anywhere yet.
/// </summary>
public interface IUploadedPhotoLookup
{
    Task<ShopPhotoClaimResult> ClaimForShopAsync(Guid userId, Guid shopId,
        ShopPhotoAttachmentKind kind, IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default);

    Task<UploadedPhotoLookupResult> ResolveAsync(
        Guid userId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default);

    Task<CheckInPhotoClaimResult> ClaimForCheckInAsync(Guid userId, Guid checkInId,
        IReadOnlyCollection<string> storageKeys, CancellationToken ct = default);

    Task ReleaseCheckInClaimAsync(Guid userId, Guid checkInId, CancellationToken ct = default);
}
