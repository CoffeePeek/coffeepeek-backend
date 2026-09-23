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
