namespace CoffeePeek.MediaService.Services;

public static class CoffeePhotoFormat
{
    public static (string ContentType, string Extension)? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return ("image/jpeg", "jpg");
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", "png");
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return ("image/gif", "gif");
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8) && (data[8..12].SequenceEqual("avif"u8) || data[8..12].SequenceEqual("avis"u8))) return ("image/avif", "avif");
        return null;
    }
}
