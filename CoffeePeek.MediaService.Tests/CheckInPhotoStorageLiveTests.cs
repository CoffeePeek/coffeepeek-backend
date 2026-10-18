using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using Minio;
using Minio.DataModel.Args;

namespace CoffeePeek.MediaService.Tests;

public class CheckInPhotoStorageLiveTests
{
    [Fact]
    [Trait("Category", "LiveMinio")]
    public async Task PrivateBucketAuditAndStreamingExecuteAgainstMinio()
    {
        var endpoint = Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_MINIO");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), "Set COFFEEPEEK_LIVE_MINIO and the dedicated test credentials.");
        var accessKey = Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_MINIO_ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_MINIO_SECRET_KEY");
        Assert.False(string.IsNullOrWhiteSpace(accessKey), "Test MinIO access key is required.");
        Assert.False(string.IsNullOrWhiteSpace(secretKey), "Test MinIO secret key is required.");
        var uri = new Uri(endpoint!);
        var client = new MinioClient().WithEndpoint(uri).WithCredentials(accessKey, secretKey)
            .WithRegion("us-east-1").WithSSL(uri.Scheme == "https").Build();
        var bucket = "cp-photo-test-" + Guid.NewGuid().ToString("N");
        using var storage = new MinIOStorageService(new MinIOOptions { CheckInBucketName = bucket }, client);
        var ct = TestContext.Current.CancellationToken;
        var created = false;
        try
        {
            await storage.EnsureCheckInBucketPrivateAsync(ct); created = true;
            Assert.True(await storage.IsCheckInBucketPrivateAsync(ct));
            var bytes = new byte[150_000]; Random.Shared.NextBytes(bytes);
            using var input = new MemoryStream(bytes);
            await storage.Put("test.jpg", "image/jpeg", input, bytes.Length, BucketType.CheckIn, ct);
            var state = new CheckInStorageState(TimeProvider.System);
            state.MarkVerified(TimeProvider.System.GetTimestamp());
            // This gate verifies the S3 data path. Dedicated IAM permissions are checked during provisioning rollout.
            var reader = new CheckInPhotoReader(client, bucket, state);
            using var output = new MemoryStream();
            await reader.CopyToAsync("test.jpg", output, ct);
            Assert.Equal(bytes, output.ToArray());
            await client.SetPolicyAsync(new SetPolicyArgs().WithBucket(bucket).WithPolicy(
                $$"""{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":"*","Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{bucket}}/*"]}]}"""), ct);
            Assert.False(await storage.IsCheckInBucketPrivateAsync(ct));
            await storage.EnsureCheckInBucketPrivateAsync(ct);
            Assert.True(await storage.IsCheckInBucketPrivateAsync(ct));
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject("test.jpg"), cleanup.Token);
                await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket), cleanup.Token);
            }
        }
    }
}
