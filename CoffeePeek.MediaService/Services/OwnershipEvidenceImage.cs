using SkiaSharp;

namespace CoffeePeek.MediaService.Services;
public static class OwnershipEvidenceImage
{
    public static bool IsValid(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.Info.Width is <= 0 or > 8192
            || codec.Info.Height is <= 0 or > 8192
            || (long)codec.Info.Width * codec.Info.Height > 32_000_000)
            return false;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        return bitmap.GetPixels() != IntPtr.Zero
            && codec.GetPixels(info, bitmap.GetPixels()) == SKCodecResult.Success;
    }
}
