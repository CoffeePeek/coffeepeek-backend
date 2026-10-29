using CoffeePeek.MediaService.Services;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;
public class OwnershipEvidenceImageTests
{
    [Fact]
    public void ARealImageIsAcceptedAndArbitraryBytesAreRejected()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.White);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Assert.True(OwnershipEvidenceImage.IsValid(image.ToArray()));
        Assert.False(OwnershipEvidenceImage.IsValid(System.Text.Encoding.UTF8.GetBytes("<svg/>")));
        Assert.False(OwnershipEvidenceImage.IsValid([]));
    }
    [Fact]
    public void AReadableHeaderWithTruncatedPixelsIsRejected()
    {
        using var bitmap = new SKBitmap(32, 32);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 7), (byte)(y * 7), (byte)((x + y) * 3)));
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = image.ToArray();
        var idat = bytes.AsSpan().IndexOf("IDAT"u8);
        Assert.True(idat > 0);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(idat - 4, 4));
        var truncated = bytes[..(idat + 4 + Math.Max(1, length / 2))];
        using var data = SKData.CreateCopy(truncated);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(32, codec.Info.Width);
        Assert.False(OwnershipEvidenceImage.IsValid(truncated));
    }
}
