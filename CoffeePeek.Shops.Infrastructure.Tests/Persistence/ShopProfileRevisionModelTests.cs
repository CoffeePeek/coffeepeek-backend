using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopProfileRevisionModelTests
{
    [Fact]
    public void ProfileRevision_IsDatabaseConcurrencyTokenAndMigrationSnapshotMatches()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=model_only;Username=model_only;Password=model_only")
            .Options);

        var property = db.Model.FindEntityType(typeof(CoffeeShop))!
            .FindProperty(nameof(CoffeeShop.ProfileRevision))!;
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(0L, property.GetDefaultValue());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
