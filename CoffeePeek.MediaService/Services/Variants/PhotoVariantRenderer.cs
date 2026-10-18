using System.Security.Cryptography;
using System.Text;
using CoffeePeek.MediaService.Configuration;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantRenderer(HttpClient http, IOptions<PhotoVariantOptions> options,
    IOptions<MinIOOptions> storage)
{
    public async Task<RenderedPhotoVariant> RenderAsync(string sourceKey, string purpose, string format, CancellationToken ct)
    {
        using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
        using var response = await http.GetAsync(BuildUri(sourceKey, purpose, format), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.UnsupportedMediaType)
            throw new InvalidDataException("Unsupported photo source.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > PhotoVariantOptions.MaxResultBytes)
            throw new InvalidDataException("Rendered photo exceeds byte limit.");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = await body.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > PhotoVariantOptions.MaxResultBytes)
                throw new InvalidDataException("Rendered photo exceeds byte limit.");
            buffer.Write(chunk, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return Validate(buffer.ToArray(), purpose, format);
    }

    internal Uri BuildUri(string sourceKey, string purpose, string format)
    {
        // Immutable keys come exclusively from the confirmed Media binding, never from a request URL.
        if (!sourceKey.StartsWith("check-ins/", StringComparison.Ordinal)
            || sourceKey.Contains("..", StringComparison.Ordinal) || sourceKey.Any(c => c is '?' or '#' or '\\' || char.IsControl(c)))
            throw new InvalidDataException("Invalid private source key.");
        var preset = PhotoVariantRecipe.Preset(purpose, format);
        var source = $"s3://{storage.Value.CheckInBucketName}/{sourceKey}";
        var path = $"/{preset}/{Base64Url(Encoding.UTF8.GetBytes(source))}";
        var salt = Convert.FromHexString(options.Value.SigningSalt);
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var data = new byte[salt.Length + pathBytes.Length];
        salt.CopyTo(data, 0); pathBytes.CopyTo(data, salt.Length);
        var signature = Base64Url(HMACSHA256.HashData(Convert.FromHexString(options.Value.SigningKey), data));
        return new Uri($"{options.Value.RendererEndpoint.TrimEnd('/')}/{signature}{path}");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static RenderedPhotoVariant Validate(byte[] bytes, string purpose, string format)
    {
        _ = PhotoVariantRecipe.Preset(purpose, format);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var expected = format switch { "webp" => SKEncodedImageFormat.Webp, "jpeg" => SKEncodedImageFormat.Jpeg, _ => SKEncodedImageFormat.Png };
        var side = PhotoVariantRecipe.Sizes[purpose];
        if (codec == null || codec.EncodedFormat != expected || codec.FrameCount > 1
            || codec.Info.Width < 1 || codec.Info.Height < 1 || codec.Info.Width > side || codec.Info.Height > side)
            throw new InvalidDataException("Invalid rendered image format or dimensions.");
        // Header checks precede pixel allocation (at most 1600 * 1600 * 4 bytes).
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Incomplete rendered image.");
        return new(bytes, $"image/{format}", info.Width, info.Height, codec.Info.AlphaType != SKAlphaType.Opaque);
    }
}
