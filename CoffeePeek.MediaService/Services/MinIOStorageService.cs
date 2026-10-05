using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using Minio.Exceptions;

namespace CoffeePeek.MediaService.Services;

public class MinIOStorageService : IStorageService, IDisposable
{
    private const string IsPermanentTag = "is_permanent";
    private const int PresignedUrlExpirySeconds = 600;

    private readonly IMinioClient _internalClient;
    private readonly IMinioClient _presignClient;
    private readonly MinIOOptions _options;

    public MinIOStorageService(
        IOptions<MinIOOptions> options,
        IOptions<MediaPublicUrlOptions> mediaPublicOptions)
    {
        _options = options.Value;
        _internalClient = BuildClient(_options.Endpoint);
        var presignEndpoint = !string.IsNullOrWhiteSpace(mediaPublicOptions.Value.PublicEndpoint)
            ? mediaPublicOptions.Value.PublicEndpoint
            : _options.Endpoint;
        _presignClient = BuildClient(presignEndpoint);
    }

    public async Task<PresignedPhotoMetaData> GetPresignedUploadUrl(string fileName,
        string contentType, BucketType bucketType, CancellationToken ct = default, string? keyPrefix = null)
    {
        var ext = Path.GetExtension(fileName);
        var storageKey = string.IsNullOrWhiteSpace(keyPrefix)
            ? $"{Guid.NewGuid()}{ext}"
            : $"{keyPrefix.Trim('/')}/{Guid.NewGuid()}{ext}";

        var args = new PresignedPutObjectArgs()
            .WithBucket(GetBucketName(bucketType))
            .WithObject(storageKey)
            .WithHeaders(new Dictionary<string, string> {
                { "Content-Type", contentType },
                { "x-amz-tagging", $"{IsPermanentTag}={false}" }
            })
            .WithExpiry(PresignedUrlExpirySeconds);

        var url = await _presignClient.PresignedPutObjectAsync(args);

        return new PresignedPhotoMetaData(url, storageKey);
    }

    public async Task MarkAsPermanent(string storageKey, BucketType bucketType, CancellationToken ct = default)
    {
        var tags = new Dictionary<string, string>
        {
            [IsPermanentTag] = "true"
        };

        var args = new SetObjectTagsArgs()
            .WithBucket(GetBucketName(bucketType))
            .WithObject(storageKey)
            .WithTagging(Tagging.GetObjectTags(tags));

        await _internalClient.SetObjectTagsAsync(args, ct);
    }

    public Task Put(string storageKey, string contentType, Stream stream, long sizeBytes, BucketType bucketType, CancellationToken ct = default) =>
        _internalClient.PutObjectAsync(new PutObjectArgs().WithBucket(GetBucketName(bucketType)).WithObject(storageKey)
            .WithStreamData(stream).WithObjectSize(sizeBytes).WithContentType(contentType), ct);

    public async Task<bool> Exists(string storageKey, BucketType bucketType, CancellationToken ct = default)
    {
        try
        {
            var args = new StatObjectArgs()
                .WithBucket(GetBucketName(bucketType))
                .WithObject(storageKey);

            await _internalClient.StatObjectAsync(args, ct);
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<long?> GetObjectSize(string storageKey, BucketType bucketType, CancellationToken ct = default)
    {
        try
        {
            var args = new StatObjectArgs()
                .WithBucket(GetBucketName(bucketType))
                .WithObject(storageKey);

            var stat = await _internalClient.StatObjectAsync(args, ct);
            return stat.Size;
        }
        catch (MinioException)
        {
            return null;
        }
    }

    public async Task Delete(string storageKey, BucketType bucketType, CancellationToken ct = default)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(GetBucketName(bucketType))
            .WithObject(storageKey);

        await _internalClient.RemoveObjectAsync(args, ct);
    }

    public void Dispose()
    {
        _internalClient.Dispose();
        _presignClient.Dispose();
    }

    private IMinioClient BuildClient(string endpoint)
    {
        var uri = new Uri(endpoint);
        var builder = new MinioClient()
            .WithEndpoint(uri)
            .WithCredentials(_options.AccessKey, _options.SecretKey);

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            builder = builder.WithSSL();
        }

        return builder.Build();
    }

    private string GetBucketName(BucketType type)
    {
        return type switch
        {
            BucketType.User => _options.UserBucketName,
            BucketType.Shop => _options.ShopBucketName,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported bucket type")
        };
    }
}
