using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class MapSourceTruncationTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(20_001, true)]
    public async Task ClusterResponse_ReportsWhenInputPointsWereDropped(int shopCount, bool expectedTruncated)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var cityId = Guid.NewGuid();
        for (var index = 0; index < shopCount; index++)
        {
            var shop = new CoffeeShop(null, "Shop", null, PriceRange.Cheap, Guid.NewGuid());
            shop.SetLocation(cityId, "Street", 53m, 27m);
            db.Shops.Add(shop);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var queries = new CoffeeShopQueries(db, Mock.Of<IMapper>(),
            Mock.Of<IQueryCoffeeDrinkRepository>(), Mock.Of<IQueryShopMenuRepository>(),
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
        var result = await queries.GetMap(new GetShopsInBoundsQuery(52m, 26m, 54m, 28m, Zoom: 5),
            new MapClusteringOptions { MaxResponseItems = 10 }, ct);

        Assert.Single(result.Clusters!);
        Assert.Equal(expectedTruncated, result.IsTruncated);
    }
}
