using System.Security.Cryptography;
using CoffeePeek.MediaService.Configuration;
using SkiaSharp;

namespace CoffeePeek.MediaService.Services.Backfill;

internal static class PhotoBackfillContentInspector
{
    internal static async Task<PhotoBackfillContent> InspectAsync(PhotoBackfillObject source, bool verify, CancellationToken ct)
    {
        if (source.Length > PhotoVariantOptions.MaxSourceBytes) return new("oversized", source.Length);
        if (!verify) return new("unverified", source.Length);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await source.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > PhotoVariantOptions.MaxSourceBytes) return new("oversized", null);
            buffer.Write(chunk, 0, read);
        }
        ct.ThrowIfCancellationRequested();
        if (buffer.Length == 0) return new("corrupt", 0);
        if (source.Length.HasValue && source.Length != buffer.Length) return new("length_mismatch", buffer.Length);
        var bytes = buffer.ToArray();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return new(CoffeePhotoFormat.Detect(bytes) is not null ? "corrupt" : "unsupported", bytes.LongLength);
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width < 1 || height < 1) return new("corrupt", bytes.LongLength);
        if ((long)width * height > 40_000_000) return new("oversized", bytes.LongLength);
        // First frame matches the versioned preview recipe. Check dimensions before allocating pixels.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        ct.ThrowIfCancellationRequested();
        return result == SKCodecResult.Success
            ? new("verified", bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)))
            : new("corrupt", bytes.LongLength);
    }
}
