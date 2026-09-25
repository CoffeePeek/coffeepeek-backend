using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Kernel.Options;

/// <summary>
/// Public URLs for objects stored in MinIO (returned to clients in API responses).
/// </summary>
public class MediaPublicUrlOptions
{
    /// <summary>
    /// Public base URL for object storage (e.g. https://media.coffeepeek.by).
    /// Path-style URLs: {PublicEndpoint}/{bucket}/{storageKey}
    /// </summary>
    [Url]
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Optional imgproxy base URL (e.g. https://media.coffeepeek.by/img).
    /// Variant URLs: {ImageProxyEndpoint}/_/{preset}/plain/{bucket}/{storageKey}.
    /// Empty means every image variant falls back to the plain public URL built from <see cref="PublicEndpoint"/>.
    /// </summary>
    [OptionalHttpUrl]
    public string ImageProxyEndpoint { get; set; } = string.Empty;

    public string ShopBucketName { get; set; } = "coffeepeek.shops";

    public string AvatarBucketName { get; set; } = "coffeepeek.avatars";
}
