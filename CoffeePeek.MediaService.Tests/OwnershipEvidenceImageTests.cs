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
}
