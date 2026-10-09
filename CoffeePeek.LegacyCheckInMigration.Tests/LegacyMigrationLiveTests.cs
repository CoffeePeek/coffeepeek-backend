using System.Text.Json.Nodes;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;

namespace CoffeePeek.LegacyCheckInMigration.Tests;

public class LegacyMigrationLiveTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Category", "LivePostgres")]
    public async Task TransfersContentPhotosModerationVotesReportsAndCountersThenDropsArchivesAndReruns(bool changedSnapshotPhoto, bool sharedGalleryPhoto)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connections = new List<string>();
        try
        {
            foreach (var service in new[] { "legacyaccount", "legacyshops", "legacymoderation", "legacymedia" })
                connections.Add(await LivePostgresServer.CreateDatabaseAsync(service, TestContext.Current.CancellationToken));
            var settings = new MigrationSettings(connections[0], connections[1], connections[2], connections[3], new MinIOOptions());
            await using var accountContext = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(settings.Account).Options);
            await using var shopsContext = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(settings.Shops).Options);
            await using var moderationContext = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>().UseNpgsql(settings.Moderation).Options);
            await using var mediaContext = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(settings.Media).Options);
            await accountContext.GetService<IMigrator>().MigrateAsync("20261007132511_RetireReviewCountOnMain", ct);
            await shopsContext.GetService<IMigrator>().MigrateAsync("20261007133435_CanonicalCheckInsOnMain", ct);
            await moderationContext.GetService<IMigrator>().MigrateAsync("20261007133453_CanonicalCheckInSnapshotsOnMain", ct);
            await mediaContext.Database.MigrateAsync(ct);
            var author = User.CreateExternal("legacy@example.com", "test", "author", "legacy_author");
            var voter = User.CreateExternal("voter@example.com", "test", "voter", "legacy_voter");
            accountContext.Users.AddRange(author, voter);
            await accountContext.SaveChangesAsync(ct);
            var shop = new CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop(Guid.NewGuid(), "Legacy shop", null, PriceRange.Moderate, author.Id);
            var city = new City("Legacy city");
            shopsContext.Cities.Add(city);
            shop.SetDraftLocation(city.Id, "Legacy address");
            await shopsContext.SaveChangesAsync(ct);
            // This database deliberately stops before AddShopProfileRevision. Seed the
            // legacy Shops row with its historical columns rather than the current EF model.
            await shopsContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Shops"
                    ("Id", "Name", "Address", "CityId", "IsAddressValidated", "CreatedAtUtc",
                     "IsHidden", "PriceRange", "SlugRevision", "Status")
                VALUES
                    ({shop.Id}, {shop.Name}, {"Legacy address"}, {city.Id}, {false}, {DateTime.UtcNow},
                     {false}, {(int)PriceRange.Moderate}, {0L}, {(int)CoffeeShopStatus.Active})
                """, ct);
            var open = new List<NpgsqlConnection>();
            try
            {
                foreach (var connection in connections)
                {
                    var conn = new NpgsqlConnection(connection);
                    await conn.OpenAsync(ct);
                    open.Add(conn);
                }
                var account = new MigrationDatabase(open[0]);
                var shops = new MigrationDatabase(open[1]);
                var moderation = new MigrationDatabase(open[2]);
                var media = new MigrationDatabase(open[3]);
                var visit = LegacyMigrationPlannerTests.Visit();
                visit["UserId"] = author.Id.ToString(); visit["ShopId"] = shop.Id.ToString();
                visit["Rating_AverageRating"] = 4m;
                var review = LegacyMigrationPlannerTests.Review();
                review["UserId"] = author.Id.ToString(); review["ShopId"] = shop.Id.ToString(); review["CoffeeShopId"] = shop.Id.ToString();
                review["UserName"] = "legacy_author"; review["Rating_AverageRating"] = 4m;
                review["IsRemovedByAdmin"] = false; review["IsSoftDelete"] = false;
                var snapshot = LegacyMigrationPlannerTests.Snapshot(review);
                snapshot["IsSoftDelete"] = false;
                snapshot["ModerationShopId"] = null;
                visit["ReviewId"] = review.Id().ToString(); visit["Note"] = null; visit["Rating_Coffee"] = 0;
                await moderation.InsertAsync("ModerationReviews", snapshot.Pick("Id", "UserId", "UserName", "ShopId", "Comment", "DrinkNameRu",
                    "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating", "IsSoftDelete", "ModerationStatus", "CreatedAtUtc", "ModeratedAt", "ModerationShopId"));
                await shops.InsertAsync("Reviews", review.Pick("Id", "UserId", "UserName", "CoffeeShopId", "Comment", "DrinkNameRu", "ModerationReviewId",
                    "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating", "IsSoftDelete", "IsRemovedByAdmin", "CreatedAtUtc"));
                await shops.InsertAsync("CheckIns", visit.Pick("Id", "UserId", "ShopId", "ReviewId", "VisitedAt", "CreatedAtUtc", "Note", "DrinkNameRu",
                    "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating"));
                var invalidVisit = visit.DeepClone().AsObject(); invalidVisit["Id"] = Guid.NewGuid().ToString(); invalidVisit["ReviewId"] = null;
                await shops.InsertAsync("CheckIns", invalidVisit.Pick("Id", "UserId", "ShopId", "VisitedAt", "CreatedAtUtc", "Note", "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating"));
                foreach (var user in new[] { author.Id, voter.Id })
                    await shops.InsertAsync("ReviewHelpfulVotes", new JsonObject { ["ReviewId"] = review.Id().ToString(), ["UserId"] = user.ToString(), ["CreatedAtUtc"] = visit["CreatedAtUtc"]!.DeepClone() });
                var nativeRows = new List<JsonObject>();
                foreach (var deleted in new[] { false, true })
                {
                    var nativeVisit = LegacyMigrationPlannerTests.Visit();
                    nativeVisit["UserId"] = author.Id.ToString(); nativeVisit["ShopId"] = shop.Id.ToString();
                    var native = Assert.Single(LegacyMigrationPlanner.Plan([nativeVisit], [], [],
                        new Dictionary<Guid, string> { [author.Id] = "legacy_author" }, new HashSet<Guid> { shop.Id })).CheckIn;
                    native["RowVersion"] = Guid.NewGuid().ToString();
                    if (deleted) native["DeletedAtUtc"] = "2026-01-04T12:00:00Z";
                    await shops.InsertAsync("CanonicalCheckIns", native);
                    nativeRows.Add(native);
                }
                var unmappedTargets = new List<Guid>();
                foreach (var reason in new[] { "removed", "incomplete", "deleted", "orphaned", "collision" })
                {
                    var skipped = review.DeepClone().AsObject();
                    skipped["Id"] = (reason == "collision" ? nativeRows[0].Id() : Guid.NewGuid()).ToString();
                    skipped["ModerationReviewId"] = null;
                    if (reason == "removed") skipped["IsRemovedByAdmin"] = true;
                    if (reason == "incomplete") skipped["Rating_Coffee"] = 0;
                    if (reason == "deleted") skipped["IsSoftDelete"] = true;
                    if (reason == "orphaned") skipped["UserId"] = Guid.NewGuid().ToString();
                    await shops.InsertAsync("Reviews", skipped.Pick("Id", "UserId", "UserName", "CoffeeShopId", "Comment", "DrinkNameRu",
                        "ModerationReviewId", "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating",
                        "IsSoftDelete", "IsRemovedByAdmin", "CreatedAtUtc"));
                    unmappedTargets.Add(skipped.Id());
                }
                unmappedTargets.AddRange([Guid.NewGuid(), nativeRows[0].Id(), nativeRows[1].Id()]);
                var reportId = Guid.NewGuid();
                await shops.InsertAsync("ReviewReports", new JsonObject { ["Id"] = reportId.ToString(), ["ReviewId"] = review.Id().ToString(),
                    ["ReportedByUserId"] = voter.Id.ToString(), ["Text"] = "Report", ["Status"] = 0, ["CreatedAtUtc"] = visit["CreatedAtUtc"]!.DeepClone() });
                for (var i = 0; i < unmappedTargets.Count; i++)
                {
                    var status = i % 3;
                    await shops.InsertAsync("ReviewReports", new JsonObject { ["Id"] = Guid.NewGuid().ToString(),
                        ["ReviewId"] = unmappedTargets[i].ToString(), ["ReportedByUserId"] = voter.Id.ToString(),
                        ["Text"] = $"Preserved complaint {i}", ["Status"] = status,
                        ["CreatedAtUtc"] = "2026-01-02T12:00:00Z", ["UpdatedAtUtc"] = "2026-01-03T12:00:00Z",
                        ["ResolvedByAdminId"] = status == 0 ? null : author.Id.ToString(),
                        ["ResolvedAtUtc"] = status == 0 ? null : "2026-01-03T12:00:00Z" });
                }
                var originalReports = await shops.RowsAsync("ReviewReports");
                var expectedReports = originalReports.Select(x => x.DeepClone().AsObject()).ToList();
                expectedReports.Single(x => x.Id() == reportId)["ReviewId"] = visit.Id().ToString();
                var key = "legacy-review.jpg";
                var photo = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["OwnerId"] = author.Id.ToString(), ["StorageKey"] = key,
                    ["FileName"] = key, ["ContentType"] = "image/jpeg", ["SizeBytes"] = 6, ["SortIndex"] = 0, ["CreatedAtUtc"] = visit["CreatedAtUtc"]!.DeepClone() };
                var legacyPhoto = photo.DeepClone().AsObject(); legacyPhoto["ReviewId"] = review.Id().ToString();
                await shops.InsertAsync("ShopPhotos", legacyPhoto);
                var legacySnapshotPhoto = photo.DeepClone().AsObject(); legacySnapshotPhoto["Id"] = Guid.NewGuid().ToString(); legacySnapshotPhoto["ModerationReviewId"] = snapshot.Id().ToString();
                legacySnapshotPhoto["ModerationShopId"] = null;
                if (changedSnapshotPhoto) legacySnapshotPhoto["StorageKey"] = "missing-moderation.jpg";
                await moderation.InsertAsync("ShopPhotos", legacySnapshotPhoto);
                if (sharedGalleryPhoto)
                {
                    var galleryPhoto = photo.DeepClone().AsObject(); galleryPhoto["Id"] = Guid.NewGuid().ToString();
                    galleryPhoto["CoffeeShopId"] = shop.Id.ToString();
                    await shops.InsertAsync("ShopPhotos", galleryPhoto);
                }
                var metadata = photo.Pick("OwnerId", "StorageKey", "FileName", "ContentType", "SizeBytes");
                metadata["Id"] = Guid.NewGuid().ToString(); metadata["OwnerType"] = "Shop"; metadata["BucketType"] = "Shop";
                metadata["Status"] = "Confirmed"; metadata["UploadedAt"] = visit["CreatedAtUtc"]!.DeepClone();
                await media.InsertAsync("Photos", metadata);
                var objects = new Dictionary<(BucketType Bucket, string Key), byte[]> { [(BucketType.Shop, key)] = [255,216,255,1,2,3] };
                await using var minio = await LegacyMinioFixture.CreateAsync();
                IStorageService storage = minio?.Storage ?? Storage(objects).Object;
                if (minio != null)
                {
                    using var upload = new MemoryStream(objects[(BucketType.Shop, key)]);
                    await storage.Put(key, "image/jpeg", upload, upload.Length, BucketType.Shop, ct);
                }
                // Without a completed transfer, direct EF cleanup fails and retains archived rows.
                await Assert.ThrowsAsync<PostgresException>(() => shopsContext.Database.MigrateAsync(ct));
                Assert.Equal(6, (await shops.RowsAsync("Reviews")).Count);
                await AssertReportsAsync(shops, originalReports);
                // Simulate provider failure before the copy: no cleanup marker can be written.
                var broken = new Mock<IStorageService>();
                broken.Setup(x => x.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("test storage outage"));
                await Assert.ThrowsAsync<IOException>(() => LegacyDataTransfer.RunAsync(account, shops, moderation, media, broken.Object));
                Assert.False(await shops.IsCompleteAsync());
                await AssertReportsAsync(shops, originalReports);
                await LegacyDataTransfer.RunAsync(account, shops, moderation, media, storage);
                await AssertReportsAsync(shops, expectedReports);
                await LegacyDataTransfer.RunAsync(account, shops, moderation, media, storage);
                await AssertReportsAsync(shops, expectedReports); // retry after cross-database partial writes
                var canonical = Assert.Single(await shops.RowsAsync("CanonicalCheckIns"), x => x.Id() == visit.Id());
                Assert.Equal(visit.Id(), canonical.Id()); Assert.Equal("review text", canonical.Text("Note"));
                Assert.Equal(changedSnapshotPhoto ? 0 : 1, canonical.Number("Visibility")); Assert.Equal(1, canonical.Number("HelpfulCount"));
                Assert.Equal(voter.Id, Assert.Single(await shops.RowsAsync("CheckInHelpfulVotes")).Id("UserId"));
                await AssertReportsAsync(shops, expectedReports);
                if (changedSnapshotPhoto) Assert.Null(canonical["FirstPublishedAtUtc"]);
                else Assert.Equal(DateTimeOffset.Parse(snapshot.Text("ModeratedAt")!),
                    DateTimeOffset.Parse(canonical.Text("FirstPublishedAtUtc")!));
                if (changedSnapshotPhoto) Assert.Empty(await moderation.RowsAsync("ModerationCheckIns"));
                else Assert.Equal(visit.Id(), Assert.Single(await moderation.RowsAsync("ModerationCheckIns")).Id("CheckInId"));
                var claimed = Assert.Single(await media.RowsAsync("Photos"), x => x.Text("BucketType") == "CheckIn");
                Assert.Equal("CheckIn", claimed.Text("BucketType")); Assert.Equal(visit.Id(), claimed.Id("CheckInId"));
                var canonicalPhoto = Assert.Single(await shops.RowsAsync("CheckInPhotos"), x => x.Id("CanonicalCheckInId") != Guid.Empty);
                Assert.Equal(claimed.Id(), canonicalPhoto.Id());
                if (minio != null) await minio.VerifyPrivateAsync(key, claimed.Text("StorageKey")!, sharedGalleryPhoto);
                else { Assert.Equal(sharedGalleryPhoto, objects.ContainsKey((BucketType.Shop, key))); Assert.True(objects.ContainsKey((BucketType.CheckIn, claimed.Text("StorageKey")!))); }
                Assert.Equal(3, (await account.RowsAsync("Users")).Single(x => x.Id() == author.Id).Number("CheckInCount"));
                await account.MarkCompleteAsync(); await moderation.MarkCompleteAsync(); await shops.MarkCompleteAsync();
                await LegacyMigrationRunner.RunAsync(settings);
                await AssertReportsAsync(shops, expectedReports);
                Assert.False(await shops.ExistsAsync("Reviews")); Assert.False(await shops.ExistsAsync("CheckIns")); Assert.False(await shops.ExistsAsync("ReviewHelpfulVotes"));
                Assert.False(await moderation.ExistsAsync("ModerationReviews"));
                Assert.DoesNotContain("ReviewCount", (await account.RowsAsync("Users"))[0].Select(x => x.Key));
                Assert.Equal(sharedGalleryPhoto ? 1 : 0, (await shops.RowsAsync("ShopPhotos")).Count);
                Assert.Single(await shops.RowsAsync("CheckInPhotos"));
                Assert.Equal(changedSnapshotPhoto ? 0 : 1, (await moderation.RowsAsync("ShopPhotos")).Count);
                Assert.Equal(10 + (changedSnapshotPhoto ? 0 : 10) + (sharedGalleryPhoto ? 15 : 0),
                    (await shops.RowsAsync("Shops")).Single(x => x.Id() == shop.Id).Number("DataCompletenessScore"));
                await shops.ExecuteAsync("UPDATE \"CanonicalCheckIns\" SET \"Visibility\"=0 WHERE \"Id\"=@id", ("id", canonical.Id()));
                Assert.Equal(10 + (sharedGalleryPhoto ? 15 : 0),
                    (await shops.RowsAsync("Shops")).Single(x => x.Id() == shop.Id).Number("DataCompletenessScore"));
                await LegacyMigrationRunner.RunAsync(settings);
                await AssertReportsAsync(shops, expectedReports);
                Assert.Equal(3, (await shops.RowsAsync("CanonicalCheckIns")).Count);
            }
            finally { foreach (var conn in open) await conn.DisposeAsync(); }
        }
        finally { foreach (var connection in connections) await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static async Task AssertReportsAsync(MigrationDatabase shops, IReadOnlyList<JsonObject> expected)
    {
        var actual = (await shops.RowsAsync("ReviewReports")).ToDictionary(x => x.Id());
        Assert.Equal(expected.Count, actual.Count);
        foreach (var report in expected)
        {
            Assert.True(actual.TryGetValue(report.Id(), out var saved), $"Missing complaint {report.Id()}");
            Assert.True(JsonNode.DeepEquals(report, saved), $"Changed complaint audit {report.Id()}");
        }
    }

    private static Mock<IStorageService> Storage(Dictionary<(BucketType Bucket, string Key), byte[]> objects)
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(x => x.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x => x.Exists(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, BucketType bucket, CancellationToken _) => objects.ContainsKey((bucket, key)));
        storage.Setup(x => x.GetObjectSize(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, BucketType bucket, CancellationToken _) => objects.TryGetValue((bucket, key), out var bytes) ? (long?)bytes.Length : null);
        storage.Setup(x => x.CopyToAsync(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string key, BucketType bucket, Stream stream, CancellationToken ct) => stream.WriteAsync(objects[(bucket, key)], ct).AsTask());
        storage.Setup(x => x.Put(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, string _, Stream stream, long size, BucketType bucket, CancellationToken ct) =>
            { using var data = new MemoryStream(); await stream.CopyToAsync(data, ct); objects[(bucket, key)] = data.ToArray(); });
        storage.Setup(x => x.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()))
            .Returns((string key, BucketType bucket, CancellationToken _) => { objects.Remove((bucket, key)); return Task.CompletedTask; });
        return storage;
    }
}
