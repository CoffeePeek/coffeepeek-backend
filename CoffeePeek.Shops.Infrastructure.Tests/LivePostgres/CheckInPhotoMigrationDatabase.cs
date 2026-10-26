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
        shop.AddPhotos([new ShopPhoto("gallery.jpg", "image/jpeg", "shops/gallery", 42, Guid.NewGuid())]);
        db.Cities.Add(city);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        // Historical rows must not depend on columns added to the current EF model.
        foreach (var row in new[] { visit, deleted })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CanonicalCheckIns" ("Id", "UserId", "ShopId", "Username", "Note", "VisitedAt",
                    "Visibility", "ContentRevision", "ApprovedRevision", "ModerationState", "ModerationSubmissionId",
                    "ModerationDecisionSequence", "DeletedAtUtc", "HelpfulCount", "FirstPublishedAtUtc", "RowVersion",
                    "Rating_Place", "Rating_Service", "Rating_Coffee", "Rating_AverageRating", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({row.Id}, {row.UserId}, {row.ShopId}, {row.Username}, {row.Note}, {row.VisitedAt},
                    {(int)row.Visibility}, {row.ContentRevision}, {row.ApprovedRevision}, {(int)row.ModerationState}, {row.ModerationSubmissionId},
                    {row.ModerationDecisionSequence}, {row.DeletedAtUtc}, {row.HelpfulCount}, {row.FirstPublishedAtUtc}, {row.RowVersion},
                    {row.Rating!.Place}, {row.Rating.Service}, {row.Rating.Coffee}, {row.Rating.AverageRating}, {row.CreatedAtUtc}, {row.UpdatedAtUtc})
                """, ct);
        foreach (var parent in new[] { visit, visit, deleted })
        {
            var photo = new ShopPhoto("legacy.jpg", "image/jpeg", "check-ins/" + Guid.NewGuid(), 42, parent.UserId);
            db.ShopPhotos.Add(photo);
            db.Entry(photo).Property("CanonicalCheckInId").CurrentValue = parent.Id;
        }
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ShopPhotos\" SET \"CreatedAtUtc\"='2024-01-01T00:00:00Z'", ct);
    }

    internal static async Task<string> ArchiveAsync(ShopsDbContext db, CancellationToken ct)
    {
        await using var connection = await LivePostgresServer.OpenAsync(db.Database.GetConnectionString()!, ct);
        await using var command = new NpgsqlCommand("SELECT jsonb_agg(to_jsonb(p) ORDER BY p.\"Id\")::text FROM \"ShopPhotos\" p", connection);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }
}
