using System.Net;
using System.Xml;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace CoffeePeek.MediaService.Services.Backfill;

/// <summary>GetObject-only identity; never provisions buckets, changes policy or copies originals.</summary>
public sealed class PhotoBackfillObjectReader : IPhotoBackfillObjectReader, IDisposable
{
    private static readonly HttpClient Transport = new(new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly IMinioClient client;
    private readonly string bucket;
    private readonly HttpClient transport;
    private readonly CheckInStorageState state;

    public PhotoBackfillObjectReader(IOptions<MinIOOptions> storage, IOptions<CheckInPhotoReadOptions> credentials,
        CheckInStorageState state)
    {
        if (credentials.Value.AccessKey == storage.Value.AccessKey)
            throw new InvalidOperationException("Archive inspection requires the read-only photo identity.");
        var endpoint = new Uri(storage.Value.Endpoint);
        client = new MinioClient().WithEndpoint(endpoint).WithRegion("us-east-1")
            .WithCredentials(credentials.Value.AccessKey, credentials.Value.SecretKey)
            .WithSSL(endpoint.Scheme == "https").Build();
        bucket = storage.Value.CheckInBucketName;
        transport = Transport;
        this.state = state;
    }

    internal PhotoBackfillObjectReader(IMinioClient client, string bucket, HttpClient transport, CheckInStorageState state)
    { this.client = client; this.bucket = bucket; this.transport = transport; this.state = state; }

    public async Task<PhotoBackfillObject> OpenAsync(string key, CancellationToken ct)
    {
        state.RequireReady();
        using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
        HttpResponseMessage? response = null;
        try
        {
            var signed = await client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(bucket).WithObject(key).WithExpiry(60)).WaitAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, signed);
            response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound && await IsMissingObjectAsync(response, ct))
                throw new PhotoBackfillObjectMissingException();
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ServiceUnavailableException("Archive storage inspection is unavailable.");
            var body = await response.Content.ReadAsStreamAsync(ct);
            return new PhotoBackfillObject(body, response.Content.Headers.ContentLength, response);
        }
        catch (PhotoBackfillObjectMissingException) { response?.Dispose(); throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { response?.Dispose(); throw; }
        catch
        {
            response?.Dispose();
            // SDK and HTTP exceptions may embed credentials, signed addresses or object keys.
            throw new ServiceUnavailableException("Archive storage inspection is unavailable.");
        }
    }

    private static async Task<bool> IsMissingObjectAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[4097];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }
        if (length > 4096) return false;
        try
        {
            using var source = new MemoryStream(bytes, 0, length, writable: false);
            using var xml = XmlReader.Create(source, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4096 });
            return xml.ReadToFollowing("Code") && xml.ReadElementContentAsString() == "NoSuchKey";
        }
        catch (XmlException) { return false; }
    }

    public void Dispose() => client.Dispose();
}
