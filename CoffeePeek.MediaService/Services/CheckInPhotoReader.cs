using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Observability;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace CoffeePeek.MediaService.Services;

public sealed class CheckInPhotoReader : ICheckInPhotoReader, IDisposable
{
    private readonly IMinioClient client;
    private readonly string bucket;
    private readonly CheckInStorageState state;

    public CheckInPhotoReader(IOptions<MinIOOptions> storage, IOptions<CheckInPhotoReadOptions> credentials,
        CheckInStorageState state)
    {
        if (credentials.Value.AccessKey == storage.Value.AccessKey)
            throw new InvalidOperationException("Photo downloads require a separate read-only storage identity.");
        var endpoint = new Uri(storage.Value.Endpoint);
        client = new MinioClient().WithEndpoint(endpoint)
            .WithCredentials(credentials.Value.AccessKey, credentials.Value.SecretKey)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps).Build();
        bucket = storage.Value.CheckInBucketName;
        this.state = state;
    }

    internal CheckInPhotoReader(IMinioClient client, string bucket, CheckInStorageState state)
    {
        this.client = client;
        this.bucket = bucket;
        this.state = state;
    }

    public async Task CopyToAsync(string key, Stream destination, CancellationToken ct)
    {
        state.RequireReady();
        using var measurement = PhotoDeliveryTelemetry.Start("minio_get");
        await client.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key)
            .WithCallbackStream((stream, token) => PhotoDeliveryTelemetry.CopyAsync(stream, destination, "media_stream", token)), ct);
        measurement.Complete();
    }

    public void Dispose() => client.Dispose();
}
