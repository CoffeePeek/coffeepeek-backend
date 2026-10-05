using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class QueryRoasterRepositoryTests
{
    [Fact]
    public async Task GetAll_OrdersByVisibleShopCountDescendingThenName()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ShopsDbContext(options);
        var popular = new Roaster("Zulu");
        var alpha = new Roaster("Alpha");
        var beta = new Roaster("Beta");
        var unused = new Roaster("A Unused");
        var hiddenOnly = new Roaster("B Hidden");
        dbContext.Roasters.AddRange(beta, hiddenOnly, unused, popular, alpha);

        var first = new CoffeeShop(null, "First", null, PriceRange.Moderate, Guid.NewGuid());
        first.SetRoasters([popular, alpha]);
        var second = new CoffeeShop(null, "Second", null, PriceRange.Moderate, Guid.NewGuid());
        second.SetRoasters([popular, beta]);
        dbContext.Shops.AddRange(first, second);
        for (var i = 0; i < 3; i++)
        {
            var hidden = new CoffeeShop(null, $"Hidden {i}", null, PriceRange.Moderate, Guid.NewGuid());
            hidden.SetRoasters([alpha, hiddenOnly]);
            hidden.SetHidden(true);
            dbContext.Shops.Add(hidden);
        }
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        var result = await new QueryRoasterRepository(dbContext).GetAll();

        result.Select(r => r.Id).Should().Equal(popular.Id, alpha.Id, beta.Id, unused.Id, hiddenOnly.Id);
    }

    [Fact]
    public async Task ExistsByNameAsync_IsCaseInsensitiveAndTreatsWildcardsLiterally()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ShopsDbContext(options);
        dbContext.Roasters.AddRange(new Roaster("Coffee_Circus"), new Roaster("Roast%House"));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new QueryRoasterRepository(dbContext);

        (await repository.ExistsByNameAsync("coffee_circus", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await repository.ExistsByNameAsync("ROAST%HOUSE", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await repository.ExistsByNameAsync("CoffeeXCircus", TestContext.Current.CancellationToken)).Should().BeFalse();
        (await repository.ExistsByNameAsync("RoastXHouse", TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_LoadsRoasterPhotos()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ShopsDbContext(options);
        var photo = new RoasterPhoto(
            "cover.jpg",
            "image/jpeg",
            "roasters/cover.jpg",
            128,
            Guid.NewGuid());
        var roaster = new Roaster("Coffee Circus");
        roaster.ReplacePhotos([photo]);
        dbContext.Roasters.Add(roaster);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        var result = await new QueryRoasterRepository(dbContext).GetAll();

        result.Should().ContainSingle()
            .Which.Photos.Should().ContainSingle()
            .Which.StorageKey.Should().Be(photo.StorageKey);
    }
}
