using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class CheckInPhotoMigrationDatabase : LiveDatabase<ShopsDbContext>
{
    public const string PreviousMigration = "20261008094000_EnforceShopMenuItemValues";
    public Guid VisitId { get; } = Guid.NewGuid();
    public string ArchiveBefore { get; private set; } = "";
    protected override string Service => "communityphotos";
    protected override string PreFeatureMigration => PreviousMigration;
    protected override ShopsDbContext Create(DbContextOptions<ShopsDbContext> options, bool enabled) => new(options);

    protected override async Task SeedLegacyAsync(ShopsDbContext db, CancellationToken ct)
    {
        await SeedAsync(db, VisitId, ct);
        ArchiveBefore = await ArchiveAsync(db, ct);
    }

    internal static async Task SeedAsync(ShopsDbContext db, Guid visitId, CancellationToken ct)
    {
        var city = new City("photo-upgrade-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("photo-upgrade", city.Id);
        var visit = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "approved",
            Rating.Create(4, 4, 4), DateTime.UtcNow.AddDays(-1), CheckInVisibility.Public, visitId);
        Assert.True(visit.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow));
        var deleted = CheckIn.CreateUnified(visit.UserId, shop.Id, "author", "deleted",
            Rating.Create(3, 3, 3), DateTime.UtcNow.AddDays(-2));
        deleted.Delete();
        db.Cities.Add(city);
        db.Shops.Add(shop);
        db.CheckIns.AddRange(visit, deleted);
        foreach (var parent in new[] { visit, visit, deleted })
        {
            var photo = new ShopPhoto("legacy.jpg", "image/jpeg", "check-ins/" + Guid.NewGuid(), 42, parent.UserId);
            db.ShopPhotos.Add(photo);
            db.Entry(photo).Property("CanonicalCheckInId").CurrentValue = parent.Id;
        }
        shop.AddPhotos([new ShopPhoto("gallery.jpg", "image/jpeg", "shops/gallery", 42, Guid.NewGuid())]);
        // Seed with the current EF model, then restore the historical schema before running its migrations.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "CanonicalCheckIns"
                ADD COLUMN "DrinkPhotoId" uuid,
                ADD COLUMN "BeanNameSnapshot" varchar(250),
                ADD COLUMN "BrewDurationSeconds" integer,
                ADD COLUMN "BrewMethodCode" varchar(20),
                ADD COLUMN "CoffeeProductId" uuid,
                ADD COLUMN "DoseGrams" numeric(8,2),
                ADD COLUMN "Kind" integer NOT NULL DEFAULT 0,
                ADD COLUMN "PreparedOn" date,
                ADD COLUMN "TasteScore" integer
            """, ct);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "CanonicalCheckIns"
                DROP COLUMN "DrinkPhotoId",
                DROP COLUMN "BeanNameSnapshot",
                DROP COLUMN "BrewDurationSeconds",
                DROP COLUMN "BrewMethodCode",
                DROP COLUMN "CoffeeProductId",
                DROP COLUMN "DoseGrams",
                DROP COLUMN "Kind",
                DROP COLUMN "PreparedOn",
                DROP COLUMN "TasteScore"
            """, ct);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ShopPhotos\" SET \"CreatedAtUtc\"='2024-01-01T00:00:00Z'", ct);
    }

    internal static async Task<string> ArchiveAsync(ShopsDbContext db, CancellationToken ct)
    {
        await using var connection = await LivePostgresServer.OpenAsync(db.Database.GetConnectionString()!, ct);
        await using var command = new NpgsqlCommand("SELECT jsonb_agg(to_jsonb(p) ORDER BY p.\"Id\")::text FROM \"ShopPhotos\" p", connection);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }
}
