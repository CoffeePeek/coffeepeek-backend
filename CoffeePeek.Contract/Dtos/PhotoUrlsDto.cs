#nullable enable
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;

namespace CoffeePeek.Contract.Dtos;

/// <summary>
/// Ready-made URLs of resized image variants served by imgproxy.
/// When no image proxy is configured, every variant equals the plain public URL of the original.
/// </summary>
public class PhotoUrlsDto
{
    /// <summary>Small list/grid thumbnail.</summary>
    public string Thumbnail { get; init; } = string.Empty;

    /// <summary>Card-sized image.</summary>
    public string Card { get; init; } = string.Empty;

    /// <summary>Detail page image.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Fullscreen viewer image.</summary>
    public string Fullscreen { get; init; } = string.Empty;

    /// <summary>
    /// Builds variant URLs from plain string settings. Takes only string arguments so it is safe to call
    /// inside Mapster maps that EF Core translates via <c>ProjectToType</c>.
    /// Returns <c>null</c> when <paramref name="storageKey"/> is empty, or when no usable URL can be built.
    /// Without <paramref name="imageProxyEndpoint"/>, all four variants fall back to the public URL.
    /// </summary>
    public static PhotoUrlsDto? Create(
        string? publicEndpoint,
        string? imageProxyEndpoint,
        string bucketName,
        string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(imageProxyEndpoint))
        {
            var fallback = MediaStorageUrlBuilder.BuildPublicUrl(publicEndpoint, bucketName, storageKey);
            if (fallback is null)
            {
                return null;
            }

            return new PhotoUrlsDto
            {
                Thumbnail = fallback,
                Card = fallback,
                Detail = fallback,
                Fullscreen = fallback
            };
        }

        var thumbnail = MediaStorageUrlBuilder.BuildVariantUrl(imageProxyEndpoint, bucketName, storageKey, ImageVariantPresets.Thumbnail);
        var card = MediaStorageUrlBuilder.BuildVariantUrl(imageProxyEndpoint, bucketName, storageKey, ImageVariantPresets.Card);
        var detail = MediaStorageUrlBuilder.BuildVariantUrl(imageProxyEndpoint, bucketName, storageKey, ImageVariantPresets.Detail);
        var fullscreen = MediaStorageUrlBuilder.BuildVariantUrl(imageProxyEndpoint, bucketName, storageKey, ImageVariantPresets.Fullscreen);

        if (thumbnail is null || card is null || detail is null || fullscreen is null)
        {
            return null;
        }

        return new PhotoUrlsDto
        {
            Thumbnail = thumbnail,
            Card = card,
            Detail = detail,
            Fullscreen = fullscreen
        };
    }

    /// <summary>
    /// Builds variant URLs from <see cref="MediaPublicUrlOptions"/>. Use in handler/factory code that works on
    /// materialized entities; inside EF projections use the string overload.
    /// </summary>
    public static PhotoUrlsDto? Create(MediaPublicUrlOptions options, string bucketName, string? storageKey) =>
        Create(options.PublicEndpoint, options.ImageProxyEndpoint, bucketName, storageKey);
}
