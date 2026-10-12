using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using ShopPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class UnknownScheduleQueryTests
{
    [Fact]
    public async Task LegacySearchAndDetails_ExposeUnknownAndExcludeItFromOpenFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (unknown, open, closed) = await SeedAsync(db, ct);
        var media = new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" };
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CoffeeDrinkDefinition>());
        var menus = new Mock<IQueryShopMenuRepository>();
        menus.Setup(x => x.GetByShopIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShopMenu?)null);
        var queries = new CoffeeShopQueries(db, MapsterConfiguration.CreateMapper(media),
            drinks.Object, menus.Object, Options.Create(media));

        var (all, count) = await queries.Search(new SearchCoffeeShopsQuery(), ct);
        Assert.Equal(3, count);
        Assert.Null(all.Single(x => x.Id == unknown.Id).IsOpen);
        Assert.True(all.Single(x => x.Id == open.Id).IsOpen);
        Assert.False(all.Single(x => x.Id == closed.Id).IsOpen);
        Assert.Null((await queries.GetDetailsById(unknown.Id, ct))?.IsOpen);

        var (opened, openedCount) = await queries.Search(new SearchCoffeeShopsQuery(IsOpen: true), ct);
        Assert.Equal(1, openedCount);
        Assert.Equal(open.Id, Assert.Single(opened).Id);

        var (shut, shutCount) = await queries.Search(new SearchCoffeeShopsQuery(IsOpen: false), ct);
        Assert.Equal(1, shutCount);
        Assert.Equal(closed.Id, Assert.Single(shut).Id);
    }

    [Fact]
    public async Task Discovery_ExposesUnknownAndExcludesItFromBothOpenFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (unknown, open, closed) = await SeedAsync(db, ct);
        var store = new DiscoveryQueryStore(db, Mock.Of<IPublicAddressService>(),
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));

        var all = await store.SearchShopsAsync(new CoffeeShopSearchRequest(), null, ct);
        Assert.Equal(3, all.TotalItems);
        Assert.Null(all.Items.Single(x => x.Address.Slug == unknown.PublicSlug).IsOpen);
        Assert.True(all.Items.Single(x => x.Address.Slug == open.PublicSlug).IsOpen);
        Assert.False(all.Items.Single(x => x.Address.Slug == closed.PublicSlug).IsOpen);

        var opened = await store.SearchShopsAsync(new CoffeeShopSearchRequest
            { Filters = new ShopFilters { IsOpen = true } }, null, ct);
        Assert.Equal(open.PublicSlug, Assert.Single(opened.Items).Address.Slug);
        var shut = await store.SearchShopsAsync(new CoffeeShopSearchRequest
            { Filters = new ShopFilters { IsOpen = false } }, null, ct);
        Assert.Equal(closed.PublicSlug, Assert.Single(shut.Items).Address.Slug);
    }

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(CoffeeShop Unknown, CoffeeShop Open, CoffeeShop Closed)> SeedAsync(
        ShopsDbContext db, CancellationToken ct)
    {
        var city = new City("Test City");
        db.Cities.Add(city);
        var unknown = Shop("unknown-hours", city.Id);
        var open = Shop("open-hours", city.Id);
        var closed = Shop("closed-hours", city.Id);
        open.AddSchedule(Enum.GetValues<DayOfWeek>()
            .Select(day => ShopSchedule.Create(day, false,
                [ShopScheduleInterval.Create(TimeSpan.Zero, TimeSpan.FromDays(1) - TimeSpan.FromTicks(10))]))
            .ToList());
        closed.AddSchedule(Enum.GetValues<DayOfWeek>()
            .Select(day => ShopSchedule.Create(day, true, [])).ToList());
        db.Shops.AddRange(unknown, open, closed);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (unknown, open, closed);
    }

    private static CoffeeShop Shop(string slug, Guid cityId)
    {
        var shop = new CoffeeShop(null, slug, null, ShopPriceRange.Cheap, Guid.NewGuid());
        shop.SetLocation(cityId, "Street", 53m, 27m);
        shop.SetContact(null, null, null, null);
        shop.SetPublicSlug(slug);
        return shop;
    }
}
