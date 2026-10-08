using System.Net;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace CoffeePeek.LegacyCheckInMigration.Tests;

public sealed class LegacyMinioFixture : IAsyncDisposable
{
    public MinIOStorageService Storage { get; }
    private readonly IMinioClient _client;
    private readonly MinIOOptions _options;

    private LegacyMinioFixture(string endpoint)
    {
        var uri = new Uri(endpoint);
        if (uri.Host != "localhost" && (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address)))
            throw new InvalidOperationException("The MinIO test requires an isolated loopback server.");
        var suffix = Guid.NewGuid().ToString("N");
        _options = new MinIOOptions
        {
            Endpoint = endpoint, AccessKey = "coffee-legacy-test", SecretKey = "coffee-legacy-test-only",
            ShopBucketName = "cp-legacy-shops-" + suffix, CheckInBucketName = "cp-legacy-checkins-" + suffix,
            UserBucketName = "cp-legacy-avatars-" + suffix
        };
        _client = new MinioClient().WithEndpoint(uri).WithCredentials(_options.AccessKey, _options.SecretKey).Build();
        Storage = new MinIOStorageService(Options.Create(_options), Options.Create(new MediaPublicUrlOptions()));
    }

    public static async Task<LegacyMinioFixture?> CreateAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_MINIO");
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var fixture = new LegacyMinioFixture(endpoint);
        try
        {
            await fixture._client.MakeBucketAsync(new MakeBucketArgs().WithBucket(fixture._options.ShopBucketName), TestContext.Current.CancellationToken);
            await fixture._client.SetPolicyAsync(new SetPolicyArgs().WithBucket(fixture._options.ShopBucketName).WithPolicy($$"""
                {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":"*","Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{fixture._options.ShopBucketName}}/*"]}]}
                """), TestContext.Current.CancellationToken);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task VerifyPrivateAsync(string source, string target, bool sharedGalleryPhoto)
    {
        using var http = new HttpClient();
        using var oldObject = await http.GetAsync($"{_options.Endpoint}/{_options.ShopBucketName}/{source}", TestContext.Current.CancellationToken);
        using var privateObject = await http.GetAsync($"{_options.Endpoint}/{_options.CheckInBucketName}/{target}", TestContext.Current.CancellationToken);
        Assert.Equal(sharedGalleryPhoto ? HttpStatusCode.OK : HttpStatusCode.Forbidden, oldObject.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, privateObject.StatusCode);
        Assert.Equal(sharedGalleryPhoto, await Storage.Exists(source, BucketType.Shop, TestContext.Current.CancellationToken));
        Assert.True(await Storage.Exists(target, BucketType.CheckIn, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var bucket in new[] { _options.ShopBucketName, _options.CheckInBucketName })
        {
            if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket))) continue;
            await foreach (var item in _client.ListObjectsEnumAsync(new ListObjectsArgs().WithBucket(bucket).WithRecursive(true)))
                await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(item.Key));
            await _client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket));
        }
        Storage.Dispose();
        _client.Dispose();
    }
}
