using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using Minio.Exceptions;
using System.Runtime.CompilerServices;
using CoffeePeek.Shared.Kernel.Observability;

[assembly: InternalsVisibleTo("CoffeePeek.MediaService.Tests")]

namespace CoffeePeek.MediaService.Services;

public class MinIOStorageService : IStorageService, IDisposable
{
    private const string IsPermanentTag = "is_permanent";
    private const int PresignedUrlExpirySeconds = 600;
    // ponytail: deployment has one Media replica; use a DB advisory lock when scaling policy writers.
    private static readonly SemaphoreSlim PolicyLock = new(1, 1);

    private readonly IMinioClient _internalClient;
    private readonly IMinioClient _presignClient;
    private readonly MinIOOptions _options;

    internal MinIOStorageService(MinIOOptions options, IMinioClient client)
    {
        _options = options;
        _internalClient = client;
        _presignClient = client;
    }

    public MinIOStorageService(
        IOptions<MinIOOptions> options,
        IOptions<MediaPublicUrlOptions> mediaPublicOptions)
    {
        _options = options.Value;
        _internalClient = MinioClients.Create(_options.Endpoint, _options.AccessKey, _options.SecretKey);
        var presignEndpoint = !string.IsNullOrWhiteSpace(mediaPublicOptions.Value.PublicEndpoint)
            ? mediaPublicOptions.Value.PublicEndpoint
            : _options.Endpoint;
        _presignClient = MinioClients.Create(presignEndpoint, _options.AccessKey, _options.SecretKey);
    }

    public async Task<PresignedPhotoMetaData> GetPresignedUploadUrl(string fileName,
        string contentType, BucketType bucketType, CancellationToken ct = default, string? keyPrefix = null)
    {
        if (bucketType == BucketType.CheckIn)
            await EnsureCheckInBucketPrivateAsync(ct);
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

    public Task MarkAsTemporary(string storageKey, BucketType bucketType, CancellationToken ct = default) =>
        _internalClient.SetObjectTagsAsync(new SetObjectTagsArgs().WithBucket(GetBucketName(bucketType))
            .WithObject(storageKey).WithTagging(Tagging.GetObjectTags(new Dictionary<string, string>
            {
                [IsPermanentTag] = "false"
            })), ct);

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
        catch (ObjectNotFoundException)
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

    public async Task EnsureCheckInBucketPrivateAsync(CancellationToken ct = default)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("minio_policy_write");
        var bucket = GetBucketName(BucketType.CheckIn);
        if (string.IsNullOrWhiteSpace(bucket) || bucket == _options.ShopBucketName || bucket == _options.UserBucketName)
            throw new InvalidOperationException("Check-in storage must use a separate private bucket.");
        if (!await _internalClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct))
            await _internalClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
        await _internalClient.RemovePolicyAsync(new RemovePolicyArgs().WithBucket(bucket), ct);
        measurement.Complete();
    }

    public async Task<bool> IsCheckInBucketPrivateAsync(CancellationToken ct = default)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("minio_policy_audit");
        var bucket = GetBucketName(BucketType.CheckIn);
        if (string.IsNullOrWhiteSpace(bucket) || bucket == _options.ShopBucketName || bucket == _options.UserBucketName)
            return false;
        if (!await _internalClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct)) return false;
        var isPrivate = await MinioClients.HasNoPolicyAsync(_internalClient, bucket, ct);
        measurement.Complete(isPrivate ? "private" : "drift");
        return isPrivate;
    }

    public async Task CopyToAsync(string key, BucketType bucket, Stream destination, CancellationToken ct = default)
    {
        if (bucket == BucketType.CheckIn) await EnsureCheckInBucketPrivateAsync(ct);
        await _internalClient.GetObjectAsync(new GetObjectArgs().WithBucket(GetBucketName(bucket)).WithObject(key)
            .WithCallbackStream((stream, token) => stream.CopyToAsync(destination, token)), ct);
    }

    public async Task<string> MoveToCheckInStorageAsync(string key, Guid photoId, CancellationToken ct = default,
        BucketType sourceBucket = BucketType.Shop)
    {
        await EnsureCheckInBucketPrivateAsync(ct);
        var target = await CopyVerifiedCheckInObjectAsync(key, $"check-ins/{photoId:N}{Path.GetExtension(key)}", sourceBucket, ct);
        await MarkAsPermanent(target, BucketType.CheckIn, ct);
        if (sourceBucket == BucketType.CheckIn && key == target) return target;
        if (sourceBucket == BucketType.Shop) await DenyOldObjectAsync(key, ct);
        await Delete(key, sourceBucket, ct);
        if (await GetObjectSize(key, sourceBucket, ct) is not null)
            throw new InvalidOperationException("Public photo object could not be removed.");
        return target;
    }

    public async Task<string> SealShelfBagObjectAsync(string key, Guid photoId, CancellationToken ct = default)
    {
        await EnsureCheckInBucketPrivateAsync(ct);
        // The presigned upload URL stays writable for its lifetime; validate and serve only this private copy.
        var target = await CopyVerifiedCheckInObjectAsync(key, ShelfBagPhotoStorageKey.Sealed(photoId, key), BucketType.CheckIn, ct);
        await MarkAsPermanent(target, BucketType.CheckIn, ct);
        if (key != target) await Delete(key, BucketType.CheckIn, ct);
        return target;
    }

    private async Task<string> CopyVerifiedCheckInObjectAsync(string key, string target, BucketType sourceBucket, CancellationToken ct)
    {
        // A photo-owned destination survives a failed DB save and a retry with a new parent ID.
        // Live uploads may still change; keep their first private copy.
        if (await GetObjectSize(target, BucketType.CheckIn, ct) != null) return target;
        var sourceSize = await GetObjectSize(key, sourceBucket, ct)
            ?? throw new InvalidOperationException("Photo object was not found.");
        var sourceHash = await HashAsync(key, sourceBucket, ct);
        await _internalClient.CopyObjectAsync(new CopyObjectArgs().WithBucket(GetBucketName(BucketType.CheckIn))
            .WithObject(target).WithCopyObjectSource(new CopySourceObjectArgs()
                .WithBucket(GetBucketName(sourceBucket)).WithObject(key)), ct);
        var targetHash = await HashAsync(target, BucketType.CheckIn, ct);
        if (await GetObjectSize(target, BucketType.CheckIn, ct) != sourceSize
            || !CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
            throw new InvalidOperationException("Private photo copy could not be verified.");
        return target;
    }

    public static string BuildObjectDenyPolicy(string existingPolicy, string bucket, string key)
    {
        if (key.IndexOfAny(['*', '?']) >= 0)
            throw new InvalidOperationException("Wildcard object keys require an explicit historical storage policy.");
        var policy = JsonNode.Parse(existingPolicy)?.AsObject() ?? throw new InvalidOperationException("Invalid bucket policy.");
        var statements = policy["Statement"]?.AsArray() ?? throw new InvalidOperationException("Invalid bucket policy statements.");
        var sid = "CheckInPrivate" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..24];
        if (!statements.Any(statement => statement?["Sid"]?.GetValue<string>() == sid))
            statements.Add(new JsonObject { ["Sid"] = sid, ["Effect"] = "Deny", ["Principal"] = "*",
                ["Action"] = new JsonArray("s3:GetObject", "s3:GetObjectVersion"), ["Resource"] = new JsonArray($"arn:aws:s3:::{bucket}/{key}") });
        return policy.ToJsonString();
    }

    private async Task DenyOldObjectAsync(string key, CancellationToken ct)
    {
        await PolicyLock.WaitAsync(ct);
        try
        {
            var bucket = GetBucketName(BucketType.Shop);
            var policy = await _internalClient.GetPolicyAsync(new GetPolicyArgs().WithBucket(bucket), ct);
            await _internalClient.SetPolicyAsync(new SetPolicyArgs().WithBucket(bucket)
                .WithPolicy(BuildObjectDenyPolicy(policy, bucket, key)), ct);
        }
        finally { PolicyLock.Release(); }
    }

    private async Task<byte[]> HashAsync(string key, BucketType bucket, CancellationToken ct)
    {
        byte[] hash = [];
        await _internalClient.GetObjectAsync(new GetObjectArgs().WithBucket(GetBucketName(bucket)).WithObject(key)
            .WithCallbackStream(async (stream, token) => { hash = await SHA256.HashDataAsync(stream, token); }), ct);
        return hash;
    }

    private string GetBucketName(BucketType type)
    {
        return type switch
        {
            BucketType.User => _options.UserBucketName,
            BucketType.Shop => _options.ShopBucketName,
            BucketType.CheckIn => _options.CheckInBucketName,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported bucket type")
        };
    }
}
