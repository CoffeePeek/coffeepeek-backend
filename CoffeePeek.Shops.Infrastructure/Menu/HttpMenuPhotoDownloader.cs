using CoffeePeek.Shops.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Menu;

public sealed class HttpMenuPhotoDownloader(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpMenuPhotoDownloader> logger) : IMenuPhotoDownloader
{
    // Each photo is base64-inlined into one OpenAI request; unbounded reads meant memory spikes and huge bills.
    public const int MaxPhotoBytes = 8 * 1024 * 1024;

    /// <summary>Reads the body, or returns null once it exceeds <see cref="MaxPhotoBytes"/>.</summary>
    public static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxPhotoBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxPhotoBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public async Task<IReadOnlyList<MenuVisionPhoto>> DownloadAsync(
        IReadOnlyList<MenuPhotoDownloadRequest> photos,
        CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient("menu-photos");
        var downloaded = new List<MenuVisionPhoto>();

        foreach (var photo in photos)
        {
            try
            {
                using var response = await http.GetAsync(photo.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Menu photo download HTTP {StatusCode} for {Url}",
                        (int)response.StatusCode,
                        photo.Url);
                    continue;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is not null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Menu photo {Url} is not an image ({ContentType})", photo.Url, contentType);
                    continue;
                }

                var bytes = await ReadCappedAsync(response.Content, ct);
                if (bytes is null)
                {
                    logger.LogWarning("Menu photo {Url} exceeds {Limit} bytes; skipped", photo.Url, MaxPhotoBytes);
                    continue;
                }

                if (bytes.Length == 0)
                {
                    logger.LogWarning("Menu photo download was empty for {Url}", photo.Url);
                    continue;
                }

                downloaded.Add(new MenuVisionPhoto(
                    bytes,
                    string.IsNullOrWhiteSpace(photo.ContentType) ? "image/jpeg" : photo.ContentType));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Menu photo download threw for {Url}", photo.Url);
            }
        }

        if (photos.Count > 0 && downloaded.Count == 0)
            logger.LogError("Could not download any of {Count} menu photos", photos.Count);

        return downloaded;
    }
}
