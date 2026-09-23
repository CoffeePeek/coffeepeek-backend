using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class MapQueryTranslationTests
{
    [Fact]
    public void MapPointsQuery_IsTranslatableByNpgsql()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;
        using var context = new ShopsDbContext(options);
        var query = new GetShopsInBoundsQuery(53.85m, 27.50m, 53.95m, 27.63m, 12);

        var sql = CoffeeShopQueries.BuildMapPointsQuery(context, query).ToQueryString();

        sql.Should().Contain("ORDER BY s.\"Id\"");
    }
}
