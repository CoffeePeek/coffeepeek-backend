namespace CoffeePeek.Shared.Kernel;

public static class MediaStorageUrlBuilder
{
    /// <summary>
    /// Builds the public path-style URL of a stored object: <c>{publicEndpoint}/{bucketName}/{storageKey}</c>.
    /// Returns <c>null</c> when any argument is null, empty or whitespace.
    /// </summary>
    public static string? BuildPublicUrl(string? publicEndpoint, string bucketName, string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || string.IsNullOrWhiteSpace(publicEndpoint)
            || string.IsNullOrWhiteSpace(bucketName))
        {
            return null;
        }

        return $"{publicEndpoint.TrimEnd('/')}/{bucketName.Trim('/')}/{storageKey.TrimStart('/')}";
    }

    /// <summary>
    /// Builds an unsigned imgproxy presets-only URL for a stored object:
    /// <c>{imageProxyEndpoint}/_/{preset}/plain/{bucketName}/{storageKey}</c>.
    /// <c>_</c> is the signature placeholder imgproxy accepts when URL signing is disabled.
    /// Bucket and key path segments are percent-escaped, because imgproxy reads <c>@</c> in a plain source
    /// as the output-extension separator and <c>?</c> / <c>%</c> would change how the source is parsed.
    /// Returns <c>null</c> when any argument is null, empty or whitespace.
    /// </summary>
    public static string? BuildVariantUrl(string? imageProxyEndpoint, string bucketName, string? storageKey, string preset)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || string.IsNullOrWhiteSpace(imageProxyEndpoint)
            || string.IsNullOrWhiteSpace(bucketName)
            || string.IsNullOrWhiteSpace(preset))
        {
            return null;
        }

        return $"{imageProxyEndpoint.TrimEnd('/')}/_/{preset.Trim()}/plain/"
               + $"{EscapePath(bucketName.Trim('/'))}/{EscapePath(storageKey.TrimStart('/'))}";
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
