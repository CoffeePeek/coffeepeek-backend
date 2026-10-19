using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Infrastructure.Tests.Faults;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using StackExchange.Redis;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsFaultRecoveryLiveTests(ShopsLiveDatabase shops, MediaLiveDatabase media)
    : IClassFixture<ShopsLiveDatabase>, IClassFixture<MediaLiveDatabase>
{
    [Theory]
    [InlineData("redis-failed")]
    [InlineData("after-delete")]
    public async Task RealRedisFailureOrCrashBeforeAck_RestartRecoversDurableInvalidation(string checkpoint)
    {
        Assert.SkipUnless(ShopsFaultControl.IsConfigured, "Requires the isolated PostgreSQL/Redis/RabbitMQ/proxy fault gate.");
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Fault " + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Original fault profile", city.Id);
        await using (var db = shops.Context(false))
        {
            db.Cities.Add(city); db.Shops.Add(shop); await db.SaveChangesAsync(ct);
        }
        var root = NewRoot();
        await ShopsFaultControl.ProxyAsync("shops-redis", true, ct);
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,abortConnect=true");
        var key = CacheKey.Shop.Detail(shop.Id).Key;
        await redis.GetDatabase().StringSetAsync(key, "old profile");
        try
        {
            await using var first = new ShopsFaultProcess("redis", root, shops.ConnectionString, shop.Id, checkpoint, execute: true);
            await ShopsFaultControl.WaitMarkerAsync(root, "redis-ready", ct, first.Process);
            if (checkpoint == "redis-failed") await ShopsFaultControl.ProxyAsync("shops-redis", false, ct);
            File.WriteAllText(ShopsFaultControl.Marker(root, "execute"), "execute");
            await ShopsFaultControl.WaitMarkerAsync(root, "committed", ct, first.Process);
            await ShopsFaultControl.WaitMarkerAsync(root, checkpoint == "after-delete" ? "redis-deleted" : checkpoint, ct, first.Process);
            if (checkpoint == "redis-failed") await ShopsFaultControl.WaitMarkerAsync(root, "telemetry-seen", ct, first.Process);
            await AssertProfileAsync();
            if (checkpoint == "redis-failed") Assert.True(await redis.GetDatabase().KeyExistsAsync(key));
            Assert.True(await IncomingCountAsync(shops.Context(false), ct) > 0);
            await first.DisposeAsync();
            await ShopsFaultControl.ProxyAsync("shops-redis", true, ct);
            File.Delete(ShopsFaultControl.Marker(root, "redis-ready"));
            await using var recovery = new ShopsFaultProcess("redis", root, shops.ConnectionString, shop.Id);
            await ShopsFaultControl.WaitMarkerAsync(root, "redis-ready", ct, recovery.Process);
            await ShopsFaultControl.UntilAsync(async () => !await redis.GetDatabase().KeyExistsAsync(key)
                && await IncomingCountAsync(shops.Context(false), ct) == 0, ct, recovery.Process);
            await AssertProfileAsync();
        }
        finally
        {
            await ShopsFaultControl.ProxyAsync("shops-redis", true, CancellationToken.None);
            await redis.GetDatabase().KeyDeleteAsync(key);
            ArchiveLogsAndDelete(root);
        }

        async Task AssertProfileAsync()
        {
            await using var db = shops.Context(false);
            var stored = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
            Assert.Equal("Committed fault profile", stored.Name);
            Assert.Equal(1, stored.ProfileRevision);
        }
    }

    [Fact]
    public async Task PostgresCommitAcknowledgementLost_RestartRecoversEvent_AndExpectedRevisionProtectsCommandRetry()
    {
        Assert.SkipUnless(ShopsFaultControl.IsConfigured, "Requires the isolated PostgreSQL/proxy fault gate.");
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Ack loss " + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Original ack profile", city.Id);
        await using (var seed = shops.Context(false))
        {
            seed.Cities.Add(city); seed.Shops.Add(shop); await seed.SaveChangesAsync(ct);
        }
        var root = NewRoot();
        var proxyConnection = new Npgsql.NpgsqlConnectionStringBuilder(shops.ConnectionString) { Port = 15432 }.ConnectionString;
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,abortConnect=true");
        var key = CacheKey.Shop.Detail(shop.Id).Key;
        await redis.GetDatabase().StringSetAsync(key, "old profile");
        await ShopsFaultControl.ProxyAsync("shops-postgres", true, ct);
        try
        {
            await using var first = new ShopsFaultProcess("postgres", root, proxyConnection, shop.Id, "postgres-commit", execute: true);
            await ShopsFaultControl.WaitMarkerAsync(root, "postgres-ready", ct, first.Process);
            var baseline = await PendingEnvelopeIdsAsync(ct);
            File.WriteAllText(ShopsFaultControl.Marker(root, "execute"), "execute");
            await ShopsFaultControl.WaitMarkerAsync(root, "profile-flushed", ct, first.Process);
            var envelope = Assert.Single(File.ReadAllLines(ShopsFaultControl.Marker(root, "private-envelopes"))
                .Select(Guid.Parse).Except(baseline));
            Assert.DoesNotContain(envelope, await PendingEnvelopeIdsAsync(ct));
            // Delay only server replies after all business SQL, then observe commit through an independent connection.
            await ShopsFaultControl.PostgresAckDelayAsync(true, ct);
            File.WriteAllText(ShopsFaultControl.Marker(root, "allow-commit"), "commit");
            await ShopsFaultControl.UntilAsync(async () =>
            {
                await using var read = shops.Context(false);
                return await read.Shops.AnyAsync(s => s.Id == shop.Id && s.ProfileRevision == 1, ct);
            }, ct, first.Process);
            Assert.False(File.Exists(ShopsFaultControl.Marker(root, "committed")));
            await ShopsFaultControl.ProxyAsync("shops-postgres", false, ct);
            await first.DisposeAsync();
            Assert.Contains(envelope, await PendingEnvelopeIdsAsync(ct));
            await ShopsFaultControl.PostgresAckDelayAsync(false, ct);
            await ShopsFaultControl.ProxyAsync("shops-postgres", true, ct);
            File.Delete(ShopsFaultControl.Marker(root, "postgres-ready"));
            await using var retry = new ShopsFaultProcess("postgres", root, shops.ConnectionString, shop.Id, "postgres-retry", execute: true);
            await ShopsFaultControl.WaitMarkerAsync(root, "postgres-ready", ct, retry.Process);
            await ShopsFaultControl.WaitMarkerAsync(root, "retry-conflict", ct, retry.Process);
            await ShopsFaultControl.UntilAsync(async () => !await redis.GetDatabase().KeyExistsAsync(key)
                && !(await PendingEnvelopeIdsAsync(ct)).Contains(envelope), ct, retry.Process);
            await using var verify = shops.Context(false);
            var persisted = await verify.Shops.SingleAsync(s => s.Id == shop.Id, ct);
            Assert.Equal(1, persisted.ProfileRevision);
            Assert.Equal("Committed fault profile", persisted.Name);
        }
        finally
        {
            await ShopsFaultControl.PostgresAckDelayAsync(false, CancellationToken.None);
            await ShopsFaultControl.ProxyAsync("shops-postgres", true, CancellationToken.None);
            await redis.GetDatabase().KeyDeleteAsync(key);
            ArchiveLogsAndDelete(root);
        }
    }

    [Fact]
    public async Task RealBrokerOutageAfterCommit_AndMediaCrashBeforeAck_RecoverPhotoExactlyOnce()
    {
        Assert.SkipUnless(ShopsFaultControl.IsConfigured, "Requires the isolated PostgreSQL/Redis/RabbitMQ/proxy fault gate.");
        var ct = TestContext.Current.CancellationToken;
        var root = NewRoot();
        var actor = Guid.NewGuid();
        var id = Guid.NewGuid();
        var key = id.ToString("N");
        var bucket = "cp-fault-" + Guid.NewGuid().ToString("N");
        var options = ShopsFaultHost.StorageOptions(bucket);
        Assert.True(new Uri(options.Endpoint).IsLoopback);
        using var minio = new MinioClient().WithEndpoint(new Uri(options.Endpoint))
            .WithCredentials(options.AccessKey, options.SecretKey).Build();
        using var storage = ShopsFaultHost.Storage(bucket);
        await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
        try
        {
            using (var bytes = new MemoryStream(new byte[] { 1, 2, 3 }))
                await storage.Put(key, "image/jpeg", bytes, bytes.Length, BucketType.Shop, ct);
            await using (var db = media.Context(false))
            {
                db.Photos.Add(new PhotoMetadata { Id = id, OwnerId = actor, OwnerType = OwnerType.User,
                    StorageKey = key, FileName = "trusted.jpg", ContentType = "image/jpeg", SizeBytes = 3,
                    UploadedAt = DateTime.UtcNow, Status = PhotoStatus.Pending, BucketType = BucketType.Shop });
                await db.SaveChangesAsync(ct);
            }
            await ShopsFaultControl.ProxyAsync("shops-rabbit", true, ct);
            await using var sender = new ShopsFaultProcess("sender", root, shops.ConnectionString, id,
                "lost-media-reply",
                mediaConnection: media.ConnectionString, bucket: bucket, actor: actor, execute: true);
            await ShopsFaultControl.WaitMarkerAsync(root, "sender-ready", ct, sender.Process);
            await ShopsFaultControl.ProxyAsync("shops-rabbit", false, ct);
            File.WriteAllText(ShopsFaultControl.Marker(root, "execute"), "execute");
            await ShopsFaultControl.WaitMarkerAsync(root, "response-lost", ct, sender.Process);
            await using (var db = media.Context(false))
            {
                var reserved = await db.Photos.SingleAsync(p => p.Id == id, ct);
                Assert.NotNull(reserved.ShopAttachmentId);
                Assert.Equal(PhotoStatus.Pending, reserved.Status);
            }
            await using (var db = shops.Context(false))
                Assert.False(await db.Roasters.AnyAsync(r => r.Name == "Fault " + id, ct));
            File.WriteAllText(ShopsFaultControl.Marker(root, "retry"), "retry-same-uploads");
            await ShopsFaultControl.WaitMarkerAsync(root, "committed", ct, sender.Process);
            await AssertPhotoAsync(PhotoStatus.Pending);
            Assert.True(await OutgoingCountAsync(ct) > 0);
            await sender.DisposeAsync();
            await ShopsFaultControl.ProxyAsync("shops-rabbit", true, ct);
            File.Delete(ShopsFaultControl.Marker(root, "sender-ready"));
            await using var resumedSender = new ShopsFaultProcess("sender", root, shops.ConnectionString, id,
                mediaConnection: media.ConnectionString, bucket: bucket, actor: actor);
            await using var receiver = new ShopsFaultProcess("media", root, media.ConnectionString, id,
                "after-media-commit", bucket: bucket);
            await ShopsFaultControl.WaitMarkerAsync(root, "media-committed", ct, receiver.Process);
            await AssertPhotoAsync(PhotoStatus.Confirmed);
            await receiver.DisposeAsync();
            File.Delete(ShopsFaultControl.Marker(root, "media-ready"));
            await using var resumedReceiver = new ShopsFaultProcess("media", root, media.ConnectionString, id, bucket: bucket);
            await ShopsFaultControl.WaitMarkerAsync(root, "media-ready", ct, resumedReceiver.Process);
            await ShopsFaultControl.UntilAsync(async () => await OutgoingCountAsync(ct) == 0
                && await IncomingCountAsync(media.Context(false), ct) == 0, ct, resumedReceiver.Process);
            // A new envelope with the same business event also has no second effect.
            var handled = int.Parse(File.ReadAllText(ShopsFaultControl.Marker(root, "media-handled")));
            File.Delete(ShopsFaultControl.Marker(root, "committed"));
            await using var replay = new ShopsFaultProcess("replay", root, shops.ConnectionString, id,
                mediaConnection: media.ConnectionString, bucket: bucket, actor: actor, execute: true);
            await ShopsFaultControl.WaitMarkerAsync(root, "committed", ct, replay.Process);
            await ShopsFaultControl.UntilAsync(async () => int.TryParse(File.ReadAllText(ShopsFaultControl.Marker(root, "media-handled")), out var count)
                && count > handled && await OutgoingCountAsync(ct) == 0
                && await IncomingCountAsync(media.Context(false), ct) == 0, ct, resumedReceiver.Process);
            await AssertPhotoAsync(PhotoStatus.Confirmed);
            Assert.True(await storage.Exists(key, BucketType.Shop, ct));
        }
        finally
        {
            await ShopsFaultControl.ProxyAsync("shops-rabbit", true, CancellationToken.None);
            await storage.Delete(key, BucketType.Shop, CancellationToken.None);
            await minio.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket), CancellationToken.None);
            ArchiveLogsAndDelete(root);
        }

        async Task AssertPhotoAsync(PhotoStatus status)
        {
            await using var mediaDb = media.Context(false);
            var photo = await mediaDb.Photos.SingleAsync(p => p.Id == id, ct);
            Assert.Equal(status, photo.Status);
            Assert.Equal(actor, photo.OwnerId);
            Assert.Equal(CoffeePeek.Contract.Dtos.ShopPhotos.ShopPhotoAttachmentKind.Roaster, photo.ShopAttachmentKind);
            Assert.NotNull(photo.ShopAttachmentId);
            await using var shopsDb = shops.Context(false);
            var roaster = await shopsDb.Roasters.Include(r => r.Photos).SingleAsync(r => r.Id == photo.ShopAttachmentId, ct);
            Assert.Single(roaster.Photos, p => p.Id == id && p.SizeBytes == 3 && p.FileName == "trusted.jpg");
            Assert.Equal(1, await shopsDb.Roasters.CountAsync(r => r.Name == "Fault " + id, ct));
        }
    }

    [Theory]
    [InlineData("before-ocr-store")]
    [InlineData("after-ocr-store")]
    [InlineData("before-ocr-apply-commit")]
    public async Task OcrProcessCrash_RestartUsesStoredResultOrRequiresOperatorWithoutRepeatingProvider(string checkpoint)
    {
        Assert.SkipUnless(ShopsFaultControl.IsConfigured, "Requires the isolated process/PostgreSQL fault gate.");
        var ct = TestContext.Current.CancellationToken;
        var city = new City("OCR fault " + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("OCR fault", city.Id);
        var menu = ShopMenu.Create(shop.Id);
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "ocr/" + Guid.NewGuid(), 3);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        await using (var db = shops.Context(false))
        {
            db.Cities.Add(city); db.Shops.Add(shop); db.ShopMenus.Add(menu); await db.SaveChangesAsync(ct);
        }
        var root = NewRoot();
        try
        {
            Guid[] baselineEnvelopes;
            await using (var baseline = shops.Context(false))
                baselineEnvelopes = await baseline.Database.SqlQueryRaw<Guid>("""
                    SELECT id AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
                    UNION SELECT id AS "Value" FROM wolverine.wolverine_outgoing_envelopes
                    """).ToArrayAsync(ct);
            await using var first = new ShopsFaultProcess("ocr", root, shops.ConnectionString, job.Id, checkpoint);
            await ShopsFaultControl.WaitMarkerAsync(root, "ocr-ready", ct, first.Process);
            File.WriteAllText(ShopsFaultControl.Marker(root, "execute"), "execute");
            await ShopsFaultControl.WaitMarkerAsync(root, checkpoint, ct, first.Process);
            await using (var db = shops.Context(false))
            {
                var stored = await db.MenuOcrJobs.SingleAsync(j => j.Id == job.Id, ct);
                Assert.Equal(checkpoint == "before-ocr-store" ? MenuOcrJobStatus.Running
                    : MenuOcrJobStatus.ResultStored, stored.Status);
                if (stored.ResultJson is not null)
                {
                    using var result = System.Text.Json.JsonDocument.Parse(stored.ResultJson);
                    Assert.True(result.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "Synthetic provider result must contain one complete offer: " + stored.ResultJson);
                }
                Assert.Empty((await db.ShopMenus.Include(m => m.Items).SingleAsync(m => m.Id == menu.Id, ct)).Items);
            }
            Assert.False(File.Exists(ShopsFaultControl.Marker(root, "ocr-invalidated")));
            if (checkpoint == "before-ocr-apply-commit")
            {
                var privateIds = File.ReadAllLines(ShopsFaultControl.Marker(root, "private-envelopes"))
                    .Select(Guid.Parse).Except(baselineEnvelopes).ToArray();
                Assert.NotEmpty(privateIds);
                await using var outside = shops.Context(false);
                var visible = await outside.Database.SqlQueryRaw<Guid>("""
                    SELECT id AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
                    UNION SELECT id AS "Value" FROM wolverine.wolverine_outgoing_envelopes
                    """).ToArrayAsync(ct);
                Assert.Empty(privateIds.Intersect(visible));
            }
            await first.DisposeAsync();
            File.Delete(ShopsFaultControl.Marker(root, "ocr-ready"));
            await using var recovery = new ShopsFaultProcess("ocr", root, shops.ConnectionString, job.Id);
            await ShopsFaultControl.WaitMarkerAsync(root, "committed", ct, recovery.Process);
            Assert.Equal("1", File.ReadAllText(ShopsFaultControl.Marker(root, "provider-calls")));
            await using var verify = shops.Context(false);
            var final = await verify.MenuOcrJobs.SingleAsync(j => j.Id == job.Id, ct);
            var current = await verify.ShopMenus.Include(m => m.Items).Include(m => m.Photos)
                .SingleAsync(m => m.Id == menu.Id, ct);
            if (checkpoint == "before-ocr-store")
            {
                Assert.Equal(MenuOcrJobStatus.Running, final.Status);
                Assert.Null(final.ResultJson); Assert.Empty(current.Items);
                await verify.Database.ExecuteSqlRawAsync("UPDATE \"MenuOcrJobs\" SET \"StartedAtUtc\"=now()-interval '16 minutes' WHERE \"Id\"={0}", [job.Id], ct);
                File.Delete(ShopsFaultControl.Marker(root, "committed"));
                await using var operation = new ShopsFaultProcess("ocr", root, shops.ConnectionString, job.Id, "recover-ocr");
                await ShopsFaultControl.WaitMarkerAsync(root, "committed", ct, operation.Process);
                await ShopsFaultControl.WaitMarkerAsync(root, "ocr-invalidated", ct, operation.Process);
                await using var late = shops.Context(false);
                Assert.False(await new MenuOcrJobStore(late, Mock.Of<IApplyShopMenuService>()).StoreResultAsync(job.Id, "{\"success\":true}", ct));
            }
            else
            {
                Assert.Equal(MenuOcrJobStatus.Applied, final.Status);
                var item = Assert.Single(current.Items); Assert.Equal(8m, item.Price);
                Assert.Single(current.Photos);
                await ShopsFaultControl.WaitMarkerAsync(root, "ocr-invalidated", ct, recovery.Process);
            }
        }
        finally { ArchiveLogsAndDelete(root); }
    }

    private async Task<int> OutgoingCountAsync(CancellationToken ct)
    {
        await using var db = shops.Context(false);
        return await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM wolverine.wolverine_outgoing_envelopes
            """).SingleAsync(ct);
    }

    private async Task<Guid[]> PendingEnvelopeIdsAsync(CancellationToken ct)
    {
        await using var db = shops.Context(false);
        return await db.Database.SqlQueryRaw<Guid>("""
            SELECT id AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
            UNION SELECT id AS "Value" FROM wolverine.wolverine_outgoing_envelopes
            """).ToArrayAsync(ct);
    }

    private static async Task<int> IncomingCountAsync(DbContext db, CancellationToken ct)
    {
        await using (db)
            return await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::integer AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
                """).SingleAsync(ct);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ArchiveLogsAndDelete(string root)
    {
        var fullPath = Path.GetFullPath(root);
        if (!fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("cp-fault-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fault-test directory.");
        if (Environment.GetEnvironmentVariable("COFFEEPEEK_FAULT_RESULTS") is { } output)
        {
            Directory.CreateDirectory(output);
            foreach (var log in Directory.EnumerateFiles(fullPath, "*.log"))
                File.Copy(log, Path.Combine(output, Path.GetFileName(log)));
        }
        Directory.Delete(fullPath, recursive: true);
    }
}
