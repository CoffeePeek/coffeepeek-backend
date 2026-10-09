using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultPhotoLookup : IUploadedPhotoLookup
{
    public async Task<ShopPhotoClaimResult> ClaimForShopAsync(Guid userId, Guid shopId, ShopPhotoAttachmentKind kind,
        IReadOnlyCollection<string> storageKeys, CancellationToken ct = default)
    {
        await using var db = ShopsFaultHost.MediaContext(ShopsFaultControl.Required("CP_FAULT_MEDIA_DB"));
        using var storage = ShopsFaultHost.Storage(ShopsFaultControl.Required("CP_FAULT_BUCKET"));
        var response = await new ClaimShopPhotosHandler(new PhotoRepository(db), storage).ClaimAsync(
            new ClaimShopPhotosRequest(shopId, kind, storageKeys.ToArray()), userId, ct);
        var marker = ShopsFaultControl.Marker(ShopsFaultControl.Required("CP_FAULT_ROOT"), "claim-lost");
        if (response.IsSuccess && ShopsFaultControl.Required("CP_FAULT_CHECKPOINT") == "lost-media-reply"
            && !File.Exists(marker))
        {
            File.WriteAllText(marker, "reserved-but-reply-lost");
            return ShopPhotoClaimResult.Unreachable;
        }
        return response.IsSuccess ? new(true, response.Data!) : ShopPhotoClaimResult.Failed;
    }

    public Task<UploadedPhotoLookupResult> ResolveAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<CheckInPhotoClaimResult> ClaimForCheckInAsync(Guid userId, Guid checkInId, IReadOnlyCollection<string> keys,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task ReleaseCheckInClaimAsync(Guid userId, Guid checkInId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
