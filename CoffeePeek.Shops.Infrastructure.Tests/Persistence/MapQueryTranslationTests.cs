using System.Linq.Expressions;
using System.Reflection;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilteredMap_AggregatesCheckInsOnlyWhenRatingIsRequired(bool filterByRating)
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=x").Options);
        var store = new DiscoveryQueryStore(db, null!, Options.Create(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example"
        }));
        var filters = new ShopFilters { MinRating = filterByRating ? 4m : null };
        var candidates = await MapCandidates(store, filters);
        var sql = candidates.OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Name, s.Location.Latitude, s.Location.Longitude })
            .Take(1001).ToQueryString();

        if (filterByRating)
            sql.Should().Contain("CheckIns").And.Contain("avg(");
        else
            sql.Should().NotContain("CheckIns");
    }

    private static async Task<IQueryable<CoffeeShop>> MapCandidates(DiscoveryQueryStore store, ShopFilters filters)
    {
        var method = typeof(DiscoveryQueryStore).GetMethod("MatchShopsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(store, ["", filters, null, TestContext.Current.CancellationToken, false])!;
        await task;
        var match = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var hits = (IQueryable)match.GetType().GetProperty("Hits")!.GetValue(match)!;
        var parameter = Expression.Parameter(hits.ElementType, "hit");
        var projection = Expression.Lambda(Expression.Property(parameter, "Shop"), parameter);
        var select = Expression.Call(typeof(Queryable), "Select", [hits.ElementType, typeof(CoffeeShop)],
            hits.Expression, Expression.Quote(projection));
        return hits.Provider.CreateQuery<CoffeeShop>(select);
    }
}
