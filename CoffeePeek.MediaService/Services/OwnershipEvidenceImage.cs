using SkiaSharp;

namespace CoffeePeek.MediaService.Services;
public static class OwnershipEvidenceImage
{
    public static bool IsValid(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec != null && codec.Info.Width is > 0 and <= 8192
            && codec.Info.Height is > 0 and <= 8192
            && (long)codec.Info.Width * codec.Info.Height <= 32_000_000;
    }
}
