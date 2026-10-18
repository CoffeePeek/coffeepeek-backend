using CoffeePeek.MediaService.Configuration;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace CoffeePeek.MediaService.Services.Variants;

/// <summary>Dedicated identity can manage objects and inspect policy only in the private variants bucket.</summary>
public sealed class PhotoVariantStorage : IDisposable
{
    private readonly IMinioClient client;
    private readonly string bucket;
    public PhotoVariantStorage(IOptions<MinIOOptions> storage, IOptions<PhotoVariantOptions> options)
    {
        if (options.Value.AccessKey == storage.Value.AccessKey)
            throw new InvalidOperationException("Variants require a separate storage identity.");
        var endpoint = new Uri(storage.Value.Endpoint);
        client = new MinioClient().WithEndpoint(endpoint).WithSSL(endpoint.Scheme == "https")
            .WithCredentials(options.Value.AccessKey, options.Value.SecretKey).Build();
        bucket = options.Value.BucketName;
    }

    public async Task RequirePrivateAsync(CancellationToken ct)
    {
        // A job checks policy before any writes. Request serving will use its own bounded readiness audit.
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

    public async Task PutAsync(string key, RenderedPhotoVariant image, CancellationToken ct)
    {
        using var stream = new MemoryStream(image.Bytes, writable: false);
        await client.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(key).WithStreamData(stream)
            .WithObjectSize(image.Bytes.Length).WithContentType(image.ContentType), ct);
        var stat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(key), ct);
        if (stat.Size != image.Bytes.Length) throw new IOException("Stored variant length mismatch.");
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(key), ct);

    public void Dispose() => client.Dispose();
}
