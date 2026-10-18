using System.Net;
using System.Security.Cryptography;
using System.Text;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services.Variants;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantRendererTests
{
    [Theory]
    [InlineData("webp", SKEncodedImageFormat.Webp, true)]
    [InlineData("png", SKEncodedImageFormat.Png, true)]
    [InlineData("jpeg", SKEncodedImageFormat.Jpeg, false)]
    public void ValidatesDecodedGeometryFormatAndTransparency(string format, SKEncodedImageFormat encoding, bool alpha)
    {
        var result = PhotoVariantRenderer.Validate(Image(100, 200, encoding, alpha), "thumb", format);
        Assert.Equal(100, result.Width); Assert.Equal(200, result.Height);
        Assert.Equal(alpha, result.HasAlpha); Assert.Equal("image/" + format, result.ContentType);
    }

    [Fact]
    public void RejectsCorruptWrongFormatAndOversizedImagesBeforePublication()
    {
        Assert.Throws<InvalidDataException>(() => PhotoVariantRenderer.Validate([1, 2, 3], "thumb", "webp"));
        Assert.Throws<InvalidDataException>(() => PhotoVariantRenderer.Validate(Image(10, 10, SKEncodedImageFormat.Png), "thumb", "webp"));
        Assert.Throws<InvalidDataException>(() => PhotoVariantRenderer.Validate(Image(321, 1, SKEncodedImageFormat.Png), "thumb", "png"));
        var valid = Image(100, 200, SKEncodedImageFormat.Jpeg);
        Assert.Throws<InvalidDataException>(() => PhotoVariantRenderer.Validate(valid[..(valid.Length / 2)], "thumb", "jpeg"));
    }

    [Fact]
    public void SignsPresetAndPrivateS3SourceWithoutAcceptingArbitraryUrlOrRecipe()
    {
        var renderer = Create(new HttpClient());
        var uri = renderer.BuildUri("check-ins/test.jpg", "feed", "webp");
        var segments = uri.AbsolutePath.Split('/');
        Assert.Equal("v1_feed_webp", segments[2]);
        var source = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(segments[3])));
        Assert.Equal("s3://coffeepeek.checkins/check-ins/test.jpg", source);
        using var hmac = new HMACSHA256(Convert.FromHexString(new string('1', 64)));
        var expected = hmac.ComputeHash(Convert.FromHexString(new string('2', 64))
            .Concat(Encoding.UTF8.GetBytes("/" + string.Join('/', segments.Skip(2)))).ToArray());
        Assert.Equal(expected, Convert.FromBase64String(Pad(segments[1])));
        Assert.Throws<InvalidDataException>(() => renderer.BuildUri("https://example.com/x", "feed", "webp"));
        Assert.Throws<InvalidDataException>(() => renderer.BuildUri("check-ins/../x", "feed", "webp"));
        Assert.Throws<ArgumentException>(() => renderer.BuildUri("check-ins/x", "arbitrary", "webp"));
    }

    [Fact]
    public async Task RejectsOversizedBodyEvenWithoutContentLength()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableImageStream(new byte[PhotoVariantOptions.MaxResultBytes + 1]))
            });
        var renderer = Create(new HttpClient(handler.Object));
        await Assert.ThrowsAsync<InvalidDataException>(() => renderer.RenderAsync("check-ins/x", "feed", "webp", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SourceHashUsesActualBytesAndStopsAtBudget()
    {
        using var stream = new PhotoSourceDigestStream();
        stream.Write("abc"u8);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", stream.Digest());
        var chunk = new byte[1024 * 1024];
        for (var i = 0; i < 31; i++) stream.Write(chunk);
        Assert.Throws<InvalidDataException>(() => stream.Write(chunk));
    }

    internal static byte[] Image(int width, int height, SKEncodedImageFormat format, bool alpha = false)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(alpha ? new SKColor(80, 40, 20, 100) : SKColors.Brown);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 85);
        return data.ToArray();
    }

    private static string Pad(string value) => value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
    private static PhotoVariantRenderer Create(HttpClient http) => new(http,
        Options.Create(new PhotoVariantOptions { RendererEndpoint = "http://renderer:8080", SigningKey = new string('1', 64), SigningSalt = new string('2', 64) }),
        Options.Create(new MinIOOptions()));
}
