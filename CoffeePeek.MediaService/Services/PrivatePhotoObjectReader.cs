using System.Net;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Observability;
using Minio;
using Minio.DataModel.Args;

namespace CoffeePeek.MediaService.Services;

internal static class PrivatePhotoObjectReader
{
    private static readonly HttpClient Transport = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Minio 7 GetObjectAsync performs bucket and object HEADs before its GET. A local signature avoids
    /// those requests and keeps this path compatible with GetObject-only storage identities.
    /// The short-lived URL stays inside Media: callers suppress HTTP instrumentation and scrub exceptions.
    /// </summary>
    internal static async Task<HttpResponseMessage> GetAsync(IMinioClient client, string bucket, string key,
        HttpClient? transport, CancellationToken ct)
    {
        var signed = await client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket).WithObject(key).WithExpiry(60)).WaitAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, signed);
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headers.CancelAfter(TimeSpan.FromSeconds(10));
        // The local deadline ends with the headers. Streaming remains controlled by the caller.
        return await (transport ?? Transport).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
    }

    internal static async Task CopyAsync(IMinioClient client, string bucket, string key, Stream destination,
        CancellationToken ct, HttpClient? transport = null)
    {
        using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
        try
        {
            ct.ThrowIfCancellationRequested();
            using var response = await GetAsync(client, bucket, key, transport, ct);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ServiceUnavailableException("Private photo storage is unavailable.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await PhotoDeliveryTelemetry.CopyAsync(stream, destination, "media_stream", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // SDK/HTTP exceptions and S3 error bodies can contain signed URLs, bucket names or object keys.
            throw new ServiceUnavailableException("Private photo storage is unavailable.");
        }
    }
}
