using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopsCombinedMigrationTests
{
    [Fact]
    public void PendingModuleMigrationsComposeInOrderAndMatchCurrentModel()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options);

        Assert.False(db.Database.HasPendingModelChanges());
        var migrations = db.Database.GetMigrations().ToArray();
        var expected = new[]
        {
            "20261008090000_AddMenuOcrJobs",
            "20261008091000_ProtectCityReferences",
            "20261008092000_AssignShopEquipmentPrimary",
            "20261008093000_AddShopProfileRevision",
            "20261008094000_EnforceShopMenuItemValues"
        };
        var actual = migrations.Where(id => id.StartsWith("20261008", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected, actual);

        var script = db.GetService<IMigrator>().GenerateScript(
            "20261007185127_PublishedCheckInFeedIndex", expected[^1]);
        var positions = expected.Select(id => script.IndexOf(id, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order().ToArray(), positions);
        Assert.Contains("CK_ShopMenuItems_Values", script, StringComparison.Ordinal);
        Assert.Contains("TR_CoffeeShopEquipments_FillCategory", script, StringComparison.Ordinal);
        Assert.Contains("ProfileRevision", script, StringComparison.Ordinal);
    }
}
