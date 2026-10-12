using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class HomeCheckInMigrationTests
{
    [Fact]
    public void MigrationKeepsExistingShopRowsAndConstrainsPrivateHomeShape()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres").Options);

        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261012082218_CheckInDrinkPhoto", "20261012205746_HomeCoffeeCheckIns");
        Assert.Contains("ALTER TABLE \"CanonicalCheckIns\" ALTER COLUMN \"ShopId\" DROP NOT NULL", sql,
            StringComparison.Ordinal);
        Assert.Contains("ADD \"Kind\" integer NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("ADD \"PreparedOn\" date", sql, StringComparison.Ordinal);
        Assert.Contains("ADD \"DoseGrams\" numeric(8,2)", sql, StringComparison.Ordinal);
        Assert.Contains("CK_CanonicalCheckIns_KindShape", sql, StringComparison.Ordinal);
        Assert.Contains("CK_CanonicalCheckIns_HomeDetails", sql, StringComparison.Ordinal);
        Assert.Contains("FK_CanonicalCheckIns_CoffeeProducts_CoffeeProductId", sql, StringComparison.Ordinal);
    }
}
