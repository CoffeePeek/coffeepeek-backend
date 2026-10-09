using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.Shops.Application.Abstractions;

/// <summary>Resolves shop-bucket uploads before attaching them to published content.</summary>
public static class TrustedUploadedShopPhotos
{
    public static async Task<IReadOnlyList<ClaimedShopPhotoDto>> ClaimAsync(
        IUploadedPhotoLookup lookup,
        Guid ownerId,
        Guid shopId,
        ShopPhotoAttachmentKind kind,
        IReadOnlyList<UploadedPhotoDto> input,
        CancellationToken ct)
    {
        var keys = ValidateKeys(ownerId, input);
        var claim = await lookup.ClaimForShopAsync(ownerId, shopId, kind, keys, ct);
        if (!claim.Succeeded)
        {
            if (claim.IsTransientFailure)
                throw new ServiceUnavailableException("Media photo claim is unavailable. Please retry the same upload.");
            throw new ValidationException("One or more photos were not found, owned or unused.");
        }
        var byKey = claim.Photos.GroupBy(photo => photo.StorageKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (byKey.Count != keys.Length || keys.Any(key =>
                !byKey.TryGetValue(key, out var matches) || matches.Length != 1
                || matches[0].PhotoId == Guid.Empty || matches[0].SizeBytes <= 0
                || string.IsNullOrWhiteSpace(matches[0].FileName)
                || string.IsNullOrWhiteSpace(matches[0].ContentType)))
            throw new ValidationException("Media returned incomplete photo claims.");
        return keys.Select(key => byKey[key][0]).ToArray();
    }

    internal static string[] ValidateKeys(Guid ownerId, IReadOnlyList<UploadedPhotoDto> input)
    {
        if (ownerId == Guid.Empty || input is null || input.Count is 0 or > 20
            || input.Any(photo => photo is null || string.IsNullOrWhiteSpace(photo.StorageKey)
                || photo.StorageKey.Length > 255))
            throw new ValidationException("Invalid photo upload list.");

        var keys = input.Select(photo => photo.StorageKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ValidationException("Photo upload list contains duplicate storage keys.");
        return keys;
    }
}
