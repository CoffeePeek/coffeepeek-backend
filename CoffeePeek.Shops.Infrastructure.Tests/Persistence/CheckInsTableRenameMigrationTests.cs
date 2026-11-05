using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInsTableRenameMigrationTests
{
    [Fact]
    public void RenameMigration_PreservesTheTableAndRenamesItsDatabaseObjects()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=design_only;Username=test;Password=test").Options);

        var script = db.GetService<IMigrator>().GenerateScript(
            "20261105092806_DefaultShelfProfileVisibilityToPublic",
            "20261105110816_RenameCanonicalCheckInsToCheckIns",
            MigrationsSqlGenerationOptions.Default);

        Assert.Contains("ALTER TABLE \"CanonicalCheckIns\" RENAME TO \"CheckIns\"", script);
        Assert.Contains("FK_CheckInPhotos_CheckIns_CanonicalCheckInId", script);
        Assert.Contains("IX_CheckIns_FirstPublishedAtUtc_Id", script);
        Assert.Contains("trg_checkins_completeness", script);
        Assert.DoesNotContain("CREATE TABLE \"CheckIns\"", script);
        Assert.DoesNotContain("DROP TABLE \"CanonicalCheckIns\"", script);
    }
}
