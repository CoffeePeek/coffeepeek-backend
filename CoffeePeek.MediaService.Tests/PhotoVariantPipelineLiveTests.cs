using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", "LivePhotoRenderer")]
public sealed class PhotoVariantPipelineLiveTests(MediaVariantsLiveDatabase fixture) : IClassFixture<MediaVariantsLiveDatabase>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealRendererPersistsPrivateVariantsAndCleansCancelledAttemptsWithoutDeletingOriginal(bool alpha)
    {
        var rendererEndpoint = Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_RENDERER");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(rendererEndpoint), "Set COFFEEPEEK_LIVE_RENDERER on the isolated photo test stack.");
        Assert.True(LivePostgresServer.IsConfigured);
        var ct = TestContext.Current.CancellationToken;
        var endpoint = new Uri(Required("COFFEEPEEK_LIVE_MINIO"));
        var minioOptions = new MinIOOptions { Endpoint = endpoint.ToString(),
            AccessKey = Required("COFFEEPEEK_LIVE_MINIO_ACCESS_KEY"), SecretKey = Required("COFFEEPEEK_LIVE_MINIO_SECRET_KEY") };
        var variantOptions = new PhotoVariantOptions
        {
            Enabled = true, RendererEndpoint = rendererEndpoint!, SigningKey = Required("COFFEEPEEK_LIVE_RENDERER_KEY"),
            SigningSalt = Required("COFFEEPEEK_LIVE_RENDERER_SALT"), AccessKey = Required("COFFEEPEEK_LIVE_VARIANT_USER"),
            SecretKey = Required("COFFEEPEEK_LIVE_VARIANT_PASSWORD"), CleanupDryRun = true,
            ReadEnabled = true, ReadAccessKey = Required("COFFEEPEEK_LIVE_VARIANT_READER_USER"),
            ReadSecretKey = Required("COFFEEPEEK_LIVE_VARIANT_READER_PASSWORD")
        };
        var readOptions = new CheckInPhotoReadOptions { AccessKey = Required("COFFEEPEEK_LIVE_READER_USER"),
            SecretKey = Required("COFFEEPEEK_LIVE_READER_PASSWORD") };
        using var admin = Client(minioOptions.AccessKey, minioOptions.SecretKey);
        using var writer = Client(variantOptions.AccessKey, variantOptions.SecretKey);
        using var variantReadClient = Client(variantOptions.ReadAccessKey, variantOptions.ReadSecretKey);
        using var readClient = Client(readOptions.AccessKey, readOptions.SecretKey);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        await using var db = fixture.Context();
        var id = Guid.NewGuid();
        var photo = new PhotoMetadata { Id = id, OwnerId = Guid.NewGuid(), CheckInId = Guid.NewGuid(),
            Status = PhotoStatus.Confirmed, BucketType = BucketType.CheckIn, OwnerType = OwnerType.Shop,
            StorageKey = $"check-ins/{id:N}.png", FileName = "test.png", ContentType = "image/png", UploadedAt = DateTime.UtcNow };
        var source = PhotoVariantRendererTests.Image(640, 960, SKEncodedImageFormat.Png, alpha);
        photo.SizeBytes = source.Length;
        var artifacts = new List<string>();
        try
        {
            using var stream = new MemoryStream(source);
            await admin.PutObjectAsync(new PutObjectArgs().WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey)
                .WithStreamData(stream).WithObjectSize(source.Length).WithContentType("image/png"), ct);
            db.Photos.Add(photo); await db.SaveChangesAsync(ct);
            await new PhotoVariantQueue(db).EnqueueAsync(photo, ct);
            var jobs = new PhotoVariantJobStore(db, Options.Create(variantOptions));
            var job = Assert.IsType<PhotoVariantJob>(await jobs.ClaimAsync(ct));
            var state = new CheckInStorageState(TimeProvider.System);
            state.MarkVerified(TimeProvider.System.GetTimestamp());
            using var reader = new CheckInPhotoReader(Options.Create(minioOptions), Options.Create(readOptions), state);
            using var storage = new PhotoVariantStorage(Options.Create(minioOptions), Options.Create(variantOptions));
            var renderer = new PhotoVariantRenderer(http, Options.Create(variantOptions), Options.Create(minioOptions));
            Assert.True(await new PhotoVariantProcessor(jobs, reader, renderer, storage).ProcessAsync(job, ct));
            var variants = await db.PhotoVariants.Where(v => v.PhotoId == id).ToArrayAsync(ct);
            Assert.Equal(6, variants.Length);
            artifacts.AddRange(variants.Select(v => v.ObjectKey));
            Assert.All(variants, v => Assert.True(v.Width <= 640 && v.Height <= 960)); // never upscale
            Assert.Equal(3, variants.Count(v => v.Format == (alpha ? "png" : "jpeg")));
            Assert.All(variants.Where(v => v.Purpose == "thumb"), v => Assert.Equal(320, v.Height));
            var variantState = new PhotoVariantReadState(TimeProvider.System);
            using var variantReader = new PhotoVariantReader(Options.Create(minioOptions), Options.Create(variantOptions), variantState);
            await variantReader.AuditPrivacyAsync(ct);
            variantState.Lease.MarkVerified(TimeProvider.System.GetTimestamp());
            foreach (var variant in variants)
            {
                using var anonymous = await http.GetAsync(new Uri(endpoint, $"{variantOptions.BucketName}/{variant.ObjectKey}"), ct);
                Assert.Equal(System.Net.HttpStatusCode.Forbidden, anonymous.StatusCode);
                using var bytes = new MemoryStream();
                await variantReader.CopyToAsync(variant.ObjectKey, bytes, ct);
                var decoded = PhotoVariantRenderer.Validate(bytes.ToArray(), variant.Purpose, variant.Format);
                Assert.Equal(alpha, decoded.HasAlpha);
            }
            // Least privilege and signature rejection exercise the actual provisioned policies/pinned renderer.
            await Assert.ThrowsAnyAsync<MinioException>(() => writer.StatObjectAsync(new StatObjectArgs()
                .WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey), ct));
            await Assert.ThrowsAnyAsync<MinioException>(() => readClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey), ct));
            await Assert.ThrowsAnyAsync<MinioException>(() => variantReadClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(variantOptions.BucketName).WithObject(variants[0].ObjectKey), ct));
            await Assert.ThrowsAnyAsync<MinioException>(() => variantReadClient.StatObjectAsync(new StatObjectArgs()
                .WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey), ct));
            var signed = renderer.BuildUri(photo.StorageKey, "feed", "webp");
            var unsigned = new Uri(signed.GetLeftPart(UriPartial.Authority) + "/_" + signed.AbsolutePath[signed.AbsolutePath.IndexOf('/', 1)..]);
            using var denied = await http.GetAsync(unsigned, ct);
            Assert.False(denied.IsSuccessStatusCode);

            await new PhotoVariantQueue(db).CancelCheckInAsync(photo.CheckInId!.Value, DateTime.UtcNow, ct);
            await db.PhotoVariantArtifacts.Where(a => a.JobId == job.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAtUtc, DateTime.UtcNow.AddDays(-2)), ct);
            var cleanup = new PhotoVariantCleanup(db, storage, Options.Create(variantOptions), NullLogger<PhotoVariantCleanup>.Instance);
            Assert.Equal(6, await cleanup.RunAsync(ct));
            Assert.Equal(6, await db.PhotoVariants.CountAsync(v => v.PhotoId == id, ct)); // dry run
            variantOptions.CleanupDryRun = false;
            Assert.Equal(6, await cleanup.RunAsync(ct));
            Assert.Empty(await db.PhotoVariants.Where(v => v.PhotoId == id).ToArrayAsync(ct));
            Assert.Equal(source.Length, (await admin.StatObjectAsync(new StatObjectArgs()
                .WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey), ct)).Size);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            artifacts.AddRange(await db.PhotoVariantArtifacts.Where(a => db.PhotoVariantJobs.Any(j => j.Id == a.JobId && j.PhotoId == id))
                .Select(a => a.ObjectKey).ToArrayAsync(timeout.Token));
            foreach (var key in artifacts.Distinct())
                await admin.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(variantOptions.BucketName).WithObject(key), timeout.Token);
            await admin.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(minioOptions.CheckInBucketName).WithObject(photo.StorageKey), timeout.Token);
        }
        IMinioClient Client(string user, string password) => new MinioClient().WithEndpoint(endpoint).WithCredentials(user, password)
            .WithRegion("us-east-1").WithSSL(endpoint.Scheme == "https").Build();
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing {name} for isolated photo live tests.");
}
