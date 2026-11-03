namespace CoffeePeek.MediaService.Services;

/// <summary>Shelf-specific entry retaining the shared private-card raster policy.</summary>
internal static class ShelfBagPhotoValidation
{
    public const int MaxSizeBytes = PrivateCardPhotoValidation.MaxSizeBytes;
    public const int MaxSidePixels = PrivateCardPhotoValidation.MaxSidePixels;
    public static bool IsAllowedContentType(string? contentType) => PrivateCardPhotoValidation.IsAllowedContentType(contentType);
    public static Task<string?> ValidateAsync(string key, long size, IStorageService storage, CancellationToken ct) =>
        PrivateCardPhotoValidation.ValidateAsync(key, size, storage, ct);
    internal static string? Inspect(byte[] bytes) => PrivateCardPhotoValidation.Inspect(bytes);
}
