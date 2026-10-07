using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using FluentAssertions;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class LegacyReviewPhotoBackfillTests
{
    private const string SourceKey = "reviews/original.jpg";
    private const string ShopBucket = "shops";
    private const string PrivateBucket = "checkins";
    private readonly Guid _photo = Guid.NewGuid();
    private readonly Guid _checkIn = Guid.NewGuid();
    private readonly Mock<IMinioClient> _client = new();
    private readonly MinIOOptions _options = new() { ShopBucketName = ShopBucket, UserBucketName = "users", CheckInBucketName = PrivateBucket };
    private readonly Dictionary<(string Bucket, string Key), byte[]> _objects = new();
    private readonly List<string> _operations = [];
    private Func<byte[], byte[]> _copyBytes = bytes => bytes.ToArray();
    private int _copyFailures;
    private int _policyFailures;
    private string _policy = """{"Version":"2012-10-17","Statement":[{"Sid":"gallery","Effect":"Allow","Principal":"*","Action":["s3:GetObject"],"Resource":["arn:aws:s3:::shops/*"]}]}""";
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private string TargetKey => $"check-ins/{_photo:N}.jpg";

    public LegacyReviewPhotoBackfillTests()
    {
        _objects[(ShopBucket, SourceKey)] = Encoding.UTF8.GetBytes("original historical bytes");
        _client.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _client.Setup(c => c.RemovePolicyAsync(It.IsAny<RemovePolicyArgs>(), It.IsAny<CancellationToken>()))
            .Callback<RemovePolicyArgs, CancellationToken>((args, _) => _operations.Add($"private:{Arg<string>(args, "BucketName")}"))
            .Returns(Task.CompletedTask);
        _client.Setup(c => c.StatObjectAsync(It.IsAny<StatObjectArgs>(), It.IsAny<CancellationToken>()))
            .Returns<StatObjectArgs, CancellationToken>((args, _) =>
            {
                var bucket = Arg<string>(args, "BucketName");
                var key = Arg<string>(args, "ObjectName");
                _operations.Add($"stat:{bucket}:{key}");
                if (!_objects.TryGetValue((bucket, key), out var bytes))
                    return Task.FromException<ObjectStat>(new ObjectNotFoundException("Object absent."));
                return Task.FromResult(Stat(key, bytes.Length));
            });
        _client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
            .Returns<GetObjectArgs, CancellationToken>(async (args, ct) =>
            {
                var bucket = Arg<string>(args, "BucketName");
                var key = Arg<string>(args, "ObjectName");
                _operations.Add($"read:{bucket}:{key}");
                await using var bytes = new MemoryStream(_objects[(bucket, key)], writable: false);
                await Arg<Func<Stream, CancellationToken, Task>>(args, "CallBack")(bytes, ct);
                return Stat(key, bytes.Length);
            });
        _client.Setup(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()))
            .Returns<CopyObjectArgs, CancellationToken>((args, _) =>
            {
                var source = Arg<CopySourceObjectArgs>(args, "SourceObject");
                var sourceBucket = Arg<string>(source, "BucketName");
                var sourceKey = Arg<string>(source, "ObjectName");
                var bucket = Arg<string>(args, "BucketName");
                var key = Arg<string>(args, "ObjectName");
                _operations.Add($"copy:{bucket}:{key}");
                _objects[(bucket, key)] = _copyBytes(_objects[(sourceBucket, sourceKey)]);
                if (_copyFailures-- > 0) throw new MinioException("Copy response was lost.");
                return Task.CompletedTask;
            });
        _client.Setup(c => c.SetObjectTagsAsync(It.IsAny<SetObjectTagsArgs>(), It.IsAny<CancellationToken>()))
            .Callback<SetObjectTagsArgs, CancellationToken>((args, _) =>
                _operations.Add($"permanent:{Arg<string>(args, "BucketName")}:{Arg<string>(args, "ObjectName")}"))
            .Returns(Task.CompletedTask);
        _client.Setup(c => c.GetPolicyAsync(It.IsAny<GetPolicyArgs>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
        _client.Setup(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()))
            .Returns<SetPolicyArgs, CancellationToken>((args, _) =>
            {
                if (_policyFailures-- > 0) throw new MinioException("Policy unavailable.");
                Arg<string>(args, "BucketName").Should().Be(ShopBucket);
                _policy = Arg<string>(args, "PolicyJsonString");
                _operations.Add("deny-source");
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task HistoricalCopy_PreservesSourceAndVerifiedPrivateBytesAndDeniesOnlyExactOldObject()
    {
        using var storage = new MinIOStorageService(_options, _client.Object);
        var original = _objects[(ShopBucket, SourceKey)].ToArray();
        var key = await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct);
        key.Should().Be(TargetKey);
        _objects[(ShopBucket, SourceKey)].Should().Equal(original);
        _objects[(PrivateBucket, key)].Should().Equal(original);
        await using var download = new MemoryStream();
        await storage.CopyToAsync(key, BucketType.CheckIn, download, Ct);
        download.ToArray().Should().Equal(original);
        _operations.First().Should().Be($"private:{PrivateBucket}");
        _operations.IndexOf($"read:{PrivateBucket}:{key}").Should().BeLessThan(_operations.IndexOf("deny-source"));
        var statements = JsonNode.Parse(_policy)!["Statement"]!.AsArray();
        statements.Should().HaveCount(2);
        statements[0]!["Sid"]!.GetValue<string>().Should().Be("gallery");
        statements[1]!["Effect"]!.GetValue<string>().Should().Be("Deny");
        statements[1]!["Resource"]![0]!.GetValue<string>().Should().Be($"arn:aws:s3:::{ShopBucket}/{SourceKey}");
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalRetry_ReverifiesBothObjectsWithoutRecopyAndPreservesSource()
    {
        using var storage = new MinIOStorageService(_options, _client.Object);
        await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct);
        var policy = _policy;
        await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, Guid.NewGuid(), Ct);
        _policy.Should().Be(policy);
        _operations.Count(x => x == $"read:{ShopBucket}:{SourceKey}").Should().Be(2);
        _operations.Count(x => x == $"read:{PrivateBucket}:{TargetKey}").Should().Be(2);
        _client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _objects[(ShopBucket, SourceKey)].Should().Equal(_objects[(PrivateBucket, TargetKey)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalCopy_CorruptOrTruncatedCopyCannotDenyOrAdvertiseSource(bool truncated)
    {
        _copyBytes = bytes => truncated ? bytes[..^1] : Enumerable.Repeat((byte)'x', bytes.Length).ToArray();
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified*");
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.SetObjectTagsAsync(It.IsAny<SetObjectTagsArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _objects[(ShopBucket, SourceKey)].Should().Equal(Encoding.UTF8.GetBytes("original historical bytes"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalRetry_CorruptExistingDestinationFailsWithoutOverwriteOrSourceDenial(bool truncated)
    {
        var original = _objects[(ShopBucket, SourceKey)];
        _objects[(PrivateBucket, TargetKey)] = truncated ? original[..^1] : Enumerable.Repeat((byte)'x', original.Length).ToArray();
        var retainedTarget = _objects[(PrivateBucket, TargetKey)].ToArray();
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified*");
        _objects[(PrivateBucket, TargetKey)].Should().Equal(retainedTarget);
        _client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalRetry_CompleteAmbiguousCopyIsVerifiedAndReused()
    {
        _copyFailures = 1;
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<MinioException>();
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        (await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct)).Should().Be(TargetKey);
        _client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _objects[(ShopBucket, SourceKey)].Should().Equal(_objects[(PrivateBucket, TargetKey)]);
    }

    [Fact]
    public async Task HistoricalRetry_PartialAmbiguousCopyFailsClosed()
    {
        _copyFailures = 1;
        _copyBytes = bytes => bytes[..^1];
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<MinioException>();
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified*");
        _client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalRetry_PolicyFailureReusesVerifiedBytesAndRecoversDenial()
    {
        _policyFailures = 1;
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<MinioException>();
        (await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct)).Should().Be(TargetKey);
        _client.Verify(c => c.CopyObjectAsync(It.IsAny<CopyObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _operations.Count(x => x == "deny-source").Should().Be(1);
        _objects[(ShopBucket, SourceKey)].Should().Equal(_objects[(PrivateBucket, TargetKey)]);
    }

    [Fact]
    public async Task HistoricalCopy_PrivateSourceRetainsBytesWithoutChangingShopPolicy()
    {
        _objects[(PrivateBucket, SourceKey)] = _objects[(ShopBucket, SourceKey)].ToArray();
        using var storage = new MinIOStorageService(_options, _client.Object);
        (await storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct, BucketType.CheckIn)).Should().Be(TargetKey);
        _objects[(PrivateBucket, SourceKey)].Should().Equal(_objects[(PrivateBucket, TargetKey)]);
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalRetry_ExistingDestinationStillRequiresSourceEvidence()
    {
        _objects[(PrivateBucket, TargetKey)] = _objects[(ShopBucket, SourceKey)].ToArray();
        _objects.Remove((ShopBucket, SourceKey));
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        _client.Verify(c => c.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalCopy_UserBucketIsRejectedBeforeStorageChanges()
    {
        using var storage = new MinIOStorageService(_options, _client.Object);
        await FluentActions.Awaiting(() => storage.CopyHistoricalToCheckInStorageAsync(SourceKey, _photo, _checkIn, Ct, BucketType.User))
            .Should().ThrowAsync<InvalidOperationException>();
        _client.Invocations.Should().BeEmpty();
    }

    private static ObjectStat Stat(string key, long size) => ObjectStat.FromResponseHeaders(key, new Dictionary<string, string>
    {
        ["Content-Length"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Last-Modified"] = DateTimeOffset.UtcNow.ToString("r"),
        ["ETag"] = "\"archive\""
    });

    // MinIO 7 keeps argument values and its download callback internal.
    private static T Arg<T>(object args, string name) =>
        (T)args.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(args)!;
}
