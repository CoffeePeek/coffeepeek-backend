using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class AdditiveCanonicalSchemaTests
{
    [Fact]
    public void MainUpgradeKeepsArchiveDataAndSeparatesCanonicalPhotos()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=design_only;Username=test;Password=test").Options);
        var script = db.GetService<IMigrator>().GenerateScript("20261007065353_AddDominicanCoffeeOrigin",
            options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE \"CanonicalCheckIns\"", script);
        Assert.Contains("CREATE TABLE \"CheckInHelpfulVotes\"", script);
        Assert.Contains("CanonicalCheckInId", script);
        Assert.DoesNotContain("DELETE FROM", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("RENAME", script);
        Assert.Equal("CanonicalCheckIns", db.Model.FindEntityType(typeof(CheckIn))!.GetTableName());
        Assert.Contains(db.Model.FindEntityType(typeof(ShopPhoto))!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(CheckIn) && fk.Properties.Single().Name == "CanonicalCheckInId");
    }
}
