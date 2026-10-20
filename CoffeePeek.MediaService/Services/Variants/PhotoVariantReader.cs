using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Observability;
using Microsoft.Extensions.Options;
using Minio;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantReader : IPhotoVariantReader, IDisposable
{
    private readonly IMinioClient client;
    private readonly string bucket;
    private readonly PhotoVariantReadState state;

    public PhotoVariantReader(IOptions<MinIOOptions> storage, IOptions<PhotoVariantOptions> options,
        PhotoVariantReadState state)
    {
        if (options.Value.ReadAccessKey == storage.Value.AccessKey || options.Value.ReadAccessKey == options.Value.AccessKey)
            throw new InvalidOperationException("Variant downloads require a separate read-only storage identity.");
        client = MinioClients.Create(storage.Value.Endpoint, options.Value.ReadAccessKey, options.Value.ReadSecretKey);
        bucket = options.Value.BucketName;
        this.state = state;
    }

    public async Task AuditPrivacyAsync(CancellationToken ct)
    {
        if (!await MinioClients.HasNoPolicyAsync(client, bucket, ct))
            throw new InvalidOperationException("Variants bucket is not private.");
    }

    public async Task CopyToAsync(string key, Stream destination, CancellationToken ct)
    {
        state.Lease.RequireReady();
        using var measurement = PhotoDeliveryTelemetry.Start("minio_get");
        await PrivatePhotoObjectReader.CopyAsync(client, bucket, key, destination, ct);
        measurement.Complete();
    }

    public void Dispose() => client.Dispose();
}
