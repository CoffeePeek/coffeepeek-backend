using System.Text.Json.Nodes;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class BusinessOwnershipMigrationLiveTests
{
    private const string Previous = "20261026072747_AddCheckInLeaderboardEligibility";
    private const string OwnershipMigration = "20261028141439_AddBusinessOwnershipClaims";
    private static readonly Guid ShopId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid RoasterId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task UpgradePreservesExistingProfilesOwnersAndMediaIdentityAndProtectsNewHistoryOnRollback()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("ownershipupgrade", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync(Previous, ct);
            await SeedPreviousSchemaAsync(db, ct);
            var before = await ArchiveAsync(db, ct);

            await db.GetService<IMigrator>().MigrateAsync(OwnershipMigration, ct);

            Assert.False(db.Database.HasPendingModelChanges());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await ArchiveAsync(db, ct))));
            var shop = await db.Shops.AsNoTracking().Include(s => s.ShopPhotos).SingleAsync(s => s.Id == ShopId, ct);
            Assert.Equal(OwnerId, shop.OwnerUserId);
            Assert.Equal(37, shop.ProfileRevision);
            Assert.Equal("Historical shop", shop.Name);
            Assert.Equal("Shop profile before ownership requests", shop.Description);
            Assert.Equal("historical-shop", shop.PublicSlug);
            Assert.Equal("shops/historical.jpg", Assert.Single(shop.ShopPhotos).StorageKey);
            Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000005"), Assert.Single(shop.ShopPhotos).Id);
            var roaster = await db.Roasters.AsNoTracking().Include(r => r.Photos).SingleAsync(r => r.Id == RoasterId, ct);
            Assert.Null(roaster.OwnerUserId);
            Assert.Equal(0, roaster.ProfileRevision);
            Assert.Equal("Historical roaster", roaster.Name);
            Assert.Equal("Roaster profile before ownership requests", roaster.About);
            Assert.Equal("https://roaster.example", roaster.Contact!.SiteLink);
            Assert.Equal("roasters/historical.jpg", Assert.Single(roaster.Photos).StorageKey);
            Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000006"), Assert.Single(roaster.Photos).Id);
            Assert.Empty(await db.BusinessOwnershipClaims.ToArrayAsync(ct));
            Assert.Empty(await db.BusinessOwnershipChanges.ToArrayAsync(ct));

            var claim = BusinessOwnershipClaim.Create(BusinessType.Roaster, RoasterId, roaster.Name,
                roaster.PublicSlug!, Guid.NewGuid(), "manager@example.com", "Official business representative.");
            db.BusinessOwnershipClaims.Add(claim);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            var error = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(Previous, ct));
            Assert.Contains("Ownership history exists", error.MessageText);
            Assert.Contains(OwnershipMigration, await db.Database.GetAppliedMigrationsAsync(ct));
            Assert.Equal(claim.Id, (await db.BusinessOwnershipClaims.AsNoTracking().SingleAsync(ct)).Id);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await ArchiveAsync(db, ct))));
        }
        finally
        {
            await LivePostgresServer.DropDatabaseAsync(connection);
        }
    }

    // Historical inserts must work without the ownership columns introduced by the current model.
    private static Task SeedPreviousSchemaAsync(ShopsDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "Cities" ("Id", "Name", "Slug", "SlugRevision", "CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000001', 'Historical city', 'historical-city', 1, '2024-01-01T00:00:00Z');
        INSERT INTO "Shops" ("Id", "Name", "Description", "PriceRange", "Status", "IsHidden", "Slug", "SlugRevision",
            "ProfileRevision", "OwnerUserId", "CreatedAtUtc", "CityId", "Address", "IsAddressValidated")
        VALUES ('00000000-0000-0000-0000-000000000002', 'Historical shop', 'Shop profile before ownership requests',
            1, 0, false, 'historical-shop', 1, 37, '00000000-0000-0000-0000-000000000003', '2024-01-02T00:00:00Z',
            '00000000-0000-0000-0000-000000000001', 'Historical shop address', false);
        INSERT INTO "Roasters" ("Id", "Name", "About", "Slug", "SlugRevision", "CreatedAtUtc", "CityId", "Address",
            "IsAddressValidated", "SiteLink")
        VALUES ('00000000-0000-0000-0000-000000000004', 'Historical roaster', 'Roaster profile before ownership requests',
            'historical-roaster', 1, '2024-01-03T00:00:00Z', '00000000-0000-0000-0000-000000000001',
            'Historical roaster address', false, 'https://roaster.example');
        INSERT INTO "ShopPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex",
            "CoffeeShopId", "CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000005', 'shop.jpg', 'image/jpeg', 'shops/historical.jpg', 128,
            '00000000-0000-0000-0000-000000000003', 0, '00000000-0000-0000-0000-000000000002', '2024-01-04T00:00:00Z');
        INSERT INTO "RoasterPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex",
            "RoasterId", "CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000006', 'roaster.jpg', 'image/jpeg', 'roasters/historical.jpg', 256,
            '00000000-0000-0000-0000-000000000003', 0, '00000000-0000-0000-0000-000000000004', '2024-01-05T00:00:00Z');
        """, ct);

    private static Task<string> ArchiveAsync(ShopsDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object(
            'shops', (SELECT jsonb_agg(to_jsonb(s) ORDER BY s."Id") FROM "Shops" s),
            'roasters', (SELECT jsonb_agg(to_jsonb(r)-'OwnerUserId'-'ProfileRevision' ORDER BY r."Id") FROM "Roasters" r),
            'shopPhotos', (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "ShopPhotos" p),
            'roasterPhotos', (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "RoasterPhotos" p))::text AS "Value"
        """).SingleAsync(ct);
}
