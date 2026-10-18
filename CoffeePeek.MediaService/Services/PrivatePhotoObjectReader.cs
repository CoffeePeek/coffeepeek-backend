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
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static async Task CopyAsync(IMinioClient client, string bucket, string key, Stream destination,
        CancellationToken ct, HttpClient? transport = null)
    {
        // Minio 7 GetObjectAsync performs bucket and object HEADs before its GET. A local signature avoids
        // those requests and keeps this path compatible with the separate GetObject-only storage identity.
        // The short-lived URL stays inside Media; suppress HTTP instrumentation to keep it out of traces.
        using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
        try
        {
            ct.ThrowIfCancellationRequested();
            var signed = await client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(bucket).WithObject(key).WithExpiry(60)).WaitAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, signed);
            using var response = await (transport ?? Transport).SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
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
