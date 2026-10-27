using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using DomainGrind = CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate.ShelfBagGrind;

namespace CoffeePeek.Shops.Application.Features.Shelf;

/// <summary>Request parsing shared by shelf create and edit; follows the home check-in bean rules.</summary>
internal static class ShelfBagInput
{
    public const string StorageKeyPrefix = "shelf-bags/";

    public static async Task<ShelfBagDetails> ResolveCreateAsync(string? productSlug, string? customName, string? roasterName,
        int? weightGrams, CoffeePeek.Contract.Enums.ShelfBagGrind? grind, DateOnly? roastedOn, IShelfCatalogLookup catalog, CancellationToken ct)
    {
        var (productId, name, productRoaster) = await ResolveBeanAsync(productSlug, customName, catalog, ct);
        return Details(productId, name, Blank(roasterName) ?? productRoaster, weightGrams, grind, roastedOn);
    }

    public static async Task<ShelfBagDetails> ResolveUpdateAsync(Guid? keptProductId, string? productSlug, string? customName,
        string? roasterName, int? weightGrams, CoffeePeek.Contract.Enums.ShelfBagGrind? grind, DateOnly? roastedOn, ShelfBag existing,
        IShelfCatalogLookup catalog, CancellationToken ct)
    {
        if (keptProductId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(productSlug) || !string.IsNullOrWhiteSpace(customName))
                throw new ValidationException("CoffeeProductId cannot be combined with CoffeeProductSlug or CustomName.");
            if (keptProductId.Value == Guid.Empty || keptProductId != existing.CoffeeProductId)
                throw new ValidationException("CoffeeProductId must match this bag's selected product.");
            return Details(existing.CoffeeProductId, existing.Name, roasterName, weightGrams, grind, roastedOn);
        }
        var (productId, name, productRoaster) = await ResolveBeanAsync(productSlug, customName, catalog, ct);
        // Reselecting the bag's own product keeps the historical name snapshot.
        if (productId.HasValue && productId == existing.CoffeeProductId) name = existing.Name;
        return Details(productId, name, Blank(roasterName) ?? productRoaster, weightGrams, grind, roastedOn);
    }

    /// <summary>Returns the trimmed storage key of a supplied upload, or null when no photo was sent.</summary>
    public static string? StorageKey(UploadedPhotoDto? photo)
    {
        if (photo == null) return null;
        var key = photo.StorageKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length > ShelfBag.MaxStorageKeyLength
            || !key.StartsWith(StorageKeyPrefix, StringComparison.Ordinal))
            throw new ValidationException("Photo must be an upload from POST /api/Photos/shelf-bag.");
        return key;
    }

    /// <summary>Retries of the same upload resolve to the same bag, so a lost response cannot attach it twice.</summary>
    public static Guid CreateId(Guid userId, string? storageKey) => storageKey == null ? Guid.NewGuid()
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("shelf-bag-upload:" + userId.ToString("N") + ":" + storageKey))
            .AsSpan(0, 16));

    public static async Task<ShelfBagPhoto> ClaimAsync(IShelfBagPhotoStore photos, Guid userId, Guid bagId, string storageKey,
        CancellationToken ct)
    {
        var claim = await photos.ClaimAsync(userId, bagId, storageKey, ct);
        return claim switch
        {
            { Status: ShelfBagPhotoClaimStatus.Claimed, Photo: { } photo } =>
                new ShelfBagPhoto(photo.PhotoId, photo.StorageKey, photo.ContentType),
            { Status: ShelfBagPhotoClaimStatus.AttachedElsewhere } =>
                throw new ConflictException("The upload is already attached to another bag."),
            { Status: ShelfBagPhotoClaimStatus.Unavailable } =>
                throw new ServiceUnavailableException("Photo storage is unavailable; retry with the same upload."),
            _ => throw new ValidationException(
                "Photo was not found, does not belong to you, or is not a JPEG, PNG or WebP of at most 1 MiB and 1280 px.")
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ShelfBagDetails Details(Guid? productId, string? name, string? roasterName, int? weightGrams,
        CoffeePeek.Contract.Enums.ShelfBagGrind? grind, DateOnly? roastedOn)
    {
        return new ShelfBagDetails(productId, name, roasterName, weightGrams, grind is { } g ? (DomainGrind)g : null, roastedOn);
    }

    private static async Task<(Guid? Id, string? Name, string? RoasterName)> ResolveBeanAsync(string? productSlug,
        string? customName, IShelfCatalogLookup catalog, CancellationToken ct)
    {
        productSlug = string.IsNullOrWhiteSpace(productSlug) ? null : productSlug.Trim().ToLowerInvariant();
        customName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        if (productSlug != null && customName != null)
            throw new ValidationException("CoffeeProductSlug and CustomName cannot both be supplied.");
        if (customName != null && customName.Length > ShelfBag.MaxTextLength)
            throw new ValidationException($"CustomName must be at most {ShelfBag.MaxTextLength} characters.");
        if (productSlug == null) return (null, customName, null);
        if (productSlug.Length > ShelfBag.MaxTextLength) throw new ValidationException("CoffeeProductSlug is too long.");
        var product = await catalog.FindPublishedBySlugAsync(productSlug, ct)
            ?? throw new NotFoundException("Published coffee product not found.");
        return (product.Id, product.Name, product.RoasterName);
    }
}
