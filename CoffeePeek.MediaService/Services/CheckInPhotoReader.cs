using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Observability;
using Microsoft.Extensions.Options;
using Minio;

namespace CoffeePeek.MediaService.Services;

public sealed class CheckInPhotoReader : ICheckInPhotoReader, IDisposable
{
    private readonly IMinioClient client;
    private readonly string bucket;
    private readonly CheckInStorageState state;
    private readonly HttpClient? transport;

    public CheckInPhotoReader(IOptions<MinIOOptions> storage, IOptions<CheckInPhotoReadOptions> credentials,
        CheckInStorageState state)
    {
        if (credentials.Value.AccessKey == storage.Value.AccessKey)
            throw new InvalidOperationException("Photo downloads require a separate read-only storage identity.");
        var endpoint = new Uri(storage.Value.Endpoint);
        client = new MinioClient().WithEndpoint(endpoint)
            .WithCredentials(credentials.Value.AccessKey, credentials.Value.SecretKey)
            .WithRegion("us-east-1").WithSSL(endpoint.Scheme == Uri.UriSchemeHttps).Build();
        bucket = storage.Value.CheckInBucketName;
        this.state = state;
    }

    internal CheckInPhotoReader(IMinioClient client, string bucket, CheckInStorageState state,
        HttpClient? transport = null)
    {
        this.client = client;
        this.bucket = bucket;
        this.state = state;
        this.transport = transport;
    }

    public async Task CopyToAsync(string key, Stream destination, CancellationToken ct)
    {
        state.RequireReady();
        using var measurement = PhotoDeliveryTelemetry.Start("minio_get");
        await PrivatePhotoObjectReader.CopyAsync(client, bucket, key, destination, ct, transport);
        measurement.Complete();
    }

    public void Dispose() => client.Dispose();
}
