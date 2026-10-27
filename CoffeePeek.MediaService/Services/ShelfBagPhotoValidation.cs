using CoffeePeek.MediaService.Configuration;
using SkiaSharp;

namespace CoffeePeek.MediaService.Services;

/// <summary>Checks the sealed shelf-bag card: a JPEG, PNG or WebP raster of at most 1 MiB and 1280 px per side.</summary>
internal static class ShelfBagPhotoValidation
{
    public const int MaxSizeBytes = 1024 * 1024;
    public const int MaxSidePixels = 1280;

    public static bool IsAllowedContentType(string? contentType) =>
        contentType?.ToLowerInvariant() is "image/jpeg" or "image/png" or "image/webp";

    /// <summary>Returns the detected content type, or null when the object is not an acceptable card.</summary>
    public static async Task<string?> ValidateAsync(string storageKey, long size, IStorageService storage, CancellationToken ct)
    {
        if (size is <= 0 or > MaxSizeBytes) return null;
        // A fixed buffer also bounds a mismatched object response before any decoding allocation.
        var bytes = new byte[(int)size];
        using var stream = new MemoryStream(bytes, writable: true);
        try
        {
            await storage.CopyToAsync(storageKey, BucketType.CheckIn, stream, ct);
        }
        catch (NotSupportedException) { return null; } // The object exceeded its declared size.
        ct.ThrowIfCancellationRequested();
        return stream.Position == size ? Inspect(bytes) : null;
    }

    internal static string? Inspect(byte[] bytes)
    {
        var detected = CoffeePhotoFormat.Detect(bytes)?.ContentType;
        if (!IsAllowedContentType(detected)) return null;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var expected = detected switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Webp
        };
        if (codec == null || codec.EncodedFormat != expected || codec.FrameCount > 1
            || codec.Info.Width is < 1 or > MaxSidePixels || codec.Info.Height is < 1 or > MaxSidePixels)
            return null;
        // Decode once so a truncated or corrupt raster is rejected before it becomes a card.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        return result is SKCodecResult.Success ? detected : null;
    }
}
