using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using SkiaSharp;

namespace CoffeePeek.MediaService.Services;

internal static class DrinkPhotoValidation
{
    public static async Task<bool> IsValidAsync(PhotoMetadata photo, long size, IStorageService storage, CancellationToken ct)
    {
        if (!string.Equals(photo.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) || size is <= 0 or > 4 * 1024 * 1024)
            return false;
        // A fixed buffer also bounds a mismatched object response before any decoding allocation.
        var bytes = new byte[(int)size];
        using var stream = new MemoryStream(bytes, writable: true);
        try
        {
            await storage.CopyToAsync(photo.StorageKey, BucketType.CheckIn, stream, ct);
        }
        catch (NotSupportedException) { return false; } // The object exceeded its declared size.
        if (stream.Position != size || CoffeePhotoFormat.Detect(bytes)?.ContentType != "image/png") return false;
        ct.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat != SKEncodedImageFormat.Png
            || codec.Info.Width is < 1 or > 768 || codec.Info.Height is < 1 or > 768 || codec.FrameCount > 1)
            return false;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return false;
        var transparent = false;
        var visible = false;
        foreach (var pixel in bitmap.Pixels)
        {
            transparent |= pixel.Alpha < 255;
            visible |= pixel.Alpha > 0;
        }
        return transparent && visible;
    }
}
