using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class RoasterOwnershipPersistenceTests
{
    [Fact]
    public async Task OwnerListFiltersBeforePagingAndDoesNotIncludeUnassignedOrOtherOwners()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid();
        var first = new Roaster("A Own");
        var second = new Roaster("B Own");
        first.AssignOwner(owner);
        second.AssignOwner(owner);
        var foreign = new Roaster("A Foreign");
        foreign.AssignOwner(Guid.NewGuid());
        db.Roasters.AddRange(first, second, foreign, new Roaster("A Unassigned"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var (items, total) = await new QueryRoasterRepository(db)
            .GetByOwnerAsync(owner, 2, 1, TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Equal(second.Id, Assert.Single(items).Id);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void ProfileRevisionIsConcurrencyTokenAndOwnerIsIndexed()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres").Options);
        var entity = db.Model.FindEntityType(typeof(Roaster))!;

        Assert.True(entity.FindProperty(nameof(Roaster.ProfileRevision))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Roaster.OwnerUserId));
    }
}
