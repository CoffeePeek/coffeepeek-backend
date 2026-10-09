using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CityReferenceMigrationTests
{
    [Fact]
    public void GeneratedSqlChecksOrphansBeforeAddingForeignKeys()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options);
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261008090000_AddMenuOcrJobs", "20261008091000_ProtectCityReferences");

        var preflight = sql.IndexOf("City reference preflight failed", StringComparison.Ordinal);
        var firstForeignKey = sql.IndexOf("ADD CONSTRAINT \"FK_Shops_Cities_CityId\"", StringComparison.Ordinal);
        Assert.True(preflight >= 0);
        Assert.True(firstForeignKey > preflight);
        Assert.Contains("FK_Roasters_Cities_CityId", sql, StringComparison.Ordinal);
        Assert.Contains("IX_Roasters_CityId", sql, StringComparison.Ordinal);
    }
}
