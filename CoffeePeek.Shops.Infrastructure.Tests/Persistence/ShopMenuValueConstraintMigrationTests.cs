using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopMenuValueConstraintMigrationTests
{
    [Fact]
    public void MigrationChecksExistingRowsBeforeEnforcingValues()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261008093000_AddShopProfileRevision", "20261008094000_EnforceShopMenuItemValues");

        Assert.Contains("ShopMenuItems has % invalid value rows", sql, StringComparison.Ordinal);
        Assert.Contains("CK_ShopMenuItems_Values", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("invalid_count", StringComparison.Ordinal)
                    < sql.IndexOf("ADD CONSTRAINT \"CK_ShopMenuItems_Values\"", StringComparison.Ordinal));
    }
}
