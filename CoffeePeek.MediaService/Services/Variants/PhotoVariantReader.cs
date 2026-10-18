using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Observability;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

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
        var endpoint = new Uri(storage.Value.Endpoint);
        client = new MinioClient().WithEndpoint(endpoint).WithSSL(endpoint.Scheme == "https")
            .WithCredentials(options.Value.ReadAccessKey, options.Value.ReadSecretKey).WithRegion("us-east-1").Build();
        bucket = options.Value.BucketName;
        this.state = state;
    }

    public async Task AuditPrivacyAsync(CancellationToken ct)
    {
        try
        {
            var policy = await client.GetPolicyAsync(new GetPolicyArgs().WithBucket(bucket), ct);
            if (string.IsNullOrWhiteSpace(policy)) return;
            using var json = System.Text.Json.JsonDocument.Parse(policy);
            if (json.RootElement.TryGetProperty("Statement", out var statements)
                && statements.ValueKind == System.Text.Json.JsonValueKind.Array && statements.GetArrayLength() == 0) return;
        }
        catch (MinioException error) when (error.Response?.Code == "NoSuchBucketPolicy") { return; }
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
