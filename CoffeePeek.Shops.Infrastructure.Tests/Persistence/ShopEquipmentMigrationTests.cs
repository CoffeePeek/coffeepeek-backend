using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopEquipmentMigrationTests
{
    [Fact]
    public void MigrationBackfillsCategoryBeforeConstraintAndKeepsOldBinaryWritesCompatible()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options);
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261008091000_ProtectCityReferences", "20261008092000_AssignShopEquipmentPrimary");

        Assert.Contains("ALTER TABLE \"Equipments\" ALTER COLUMN \"IsPrimary\" SET DEFAULT FALSE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RENAME COLUMN \"IsPrimary\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TR_CoffeeShopEquipments_FillCategory", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE \"CoffeeShopEquipments\"", sql, StringComparison.Ordinal);
        Assert.Contains("UX_CoffeeShopEquipments_PrimaryCategory", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("UPDATE \"CoffeeShopEquipments\"", StringComparison.Ordinal)
                    < sql.IndexOf("SET NOT NULL", StringComparison.Ordinal));
        Assert.DoesNotContain("UPDATE \"CoffeeShopEquipments\" SET \"IsPrimary\" = TRUE", sql, StringComparison.Ordinal);
    }
}
