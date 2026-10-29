using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class DiscoveryShopRankingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DiscoveryQueryStore Store(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>(),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }));
    private static CoffeeShop Shop(ShopsDbContext db, string name, string? slug = null)
    {
        var shop = new CoffeeShop(null, name, null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug(slug ?? name.ToLowerInvariant());
        shop.SetLocation(Guid.NewGuid(), "Street", 53.9m, 27.56m);
        shop.SetContact(null, null, null, null);
        db.Shops.Add(shop);
        return shop;
    }
    private static CheckIn Rate(ShopsDbContext db, CoffeeShop shop, Guid user, int score, int daysAgo = 1)
    {
        var time = DateTime.UtcNow.AddDays(-daysAgo);
        var row = CheckIn.CreateUnified(user, shop.Id, "author", "visit", Rating.Create(score, score, score), time, CheckInVisibility.Public);
        row.CreatedAtUtc = time;
        row.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, time);
        db.CheckIns.Add(row);
        return row;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReliableRatingsOutrankOnePerfectAuthor_AndUnratedShopsRemainNeutral(string query)
    {
        await using var db = Database();
        var single = Shop(db, "A-single"); var unrated = Shop(db, "B-unrated");
        var poor = Shop(db, "C-poor"); var reliable = Shop(db, "Z-reliable");
        var repeatVisitor = Guid.NewGuid();
        for (var i = 1; i <= 20; i++) Rate(db, single, repeatVisitor, 5, i);
        for (var i = 0; i < 8; i++) Rate(db, reliable, Guid.NewGuid(), 4);
        Rate(db, poor, Guid.NewGuid(), 1);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var result = await store.SearchShopsAsync(new() { Q = query }, null, Ct);
        Assert.Equal(new[] { reliable.PublicSlug, single.PublicSlug, unrated.PublicSlug, poor.PublicSlug }, result.Items.Select(s => s.Address.Slug));
        Assert.Equal(5m, result.Items[1].Rating);
        Assert.Equal(20, result.Items[1].CheckInCount);
        Assert.Equal(0m, result.Items[2].Rating);
        Assert.Equal(single.PublicSlug, (await store.SearchShopsAsync(new() { Sort = "rating_desc" }, null, Ct)).Items[0].Address.Slug);
        Assert.Equal(result.Items.Select(s => s.Address.Slug),
            (await store.SearchShopsAsync(new() { Sort = "recommended" }, null, Ct)).Items.Select(s => s.Address.Slug));
    }

    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    [InlineData("drink")]
    public async Task FavoritesPersonalizeOnlyTheirOwner_AndRespectAvailability(string kind)
    {
        await using var db = Database();
        var first = Shop(db, "A-first"); var preferred = Shop(db, "Z-preferred");
        var user = Guid.NewGuid(); var entityId = preferred.Id;
        CoffeeDrinkDefinition? drink = null; ShopMenu? menu = null;
        if (kind == "roaster")
        {
            var roaster = new Roaster("Favorite"); db.Roasters.Add(roaster);
            preferred.SetRoasters([roaster]); entityId = roaster.Id;
        }
        if (kind == "drink")
        {
            drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "espresso", "Эспрессо", "Espresso", CoffeeDrinkCategory.Espresso, "", 0);
            db.CoffeeDrinkDefinitions.Add(drink); entityId = drink.Id;
            menu = ShopMenu.Create(preferred.Id);
            menu.ApplyManualItem(drink.Id, MenuItemAvailability.Present, 3, null); db.ShopMenus.Add(menu);
        }
        db.DiscoveryFavorites.Add(new(user, kind, entityId));
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        Assert.Equal(preferred.PublicSlug, (await store.SearchShopsAsync(new(), user, Ct)).Items[0].Address.Slug);
        Assert.Equal(first.PublicSlug, (await store.SearchShopsAsync(new(), null, Ct)).Items[0].Address.Slug);
        Assert.Equal(first.PublicSlug, (await store.SearchShopsAsync(new(), Guid.NewGuid(), Ct)).Items[0].Address.Slug);
        if (drink is not null)
        {
            menu!.ApplyManualItem(drink.Id, MenuItemAvailability.Absent, null, null);
            await db.SaveChangesAsync(Ct);
            Assert.Equal(first.PublicSlug, (await store.SearchShopsAsync(new(), user, Ct)).Items[0].Address.Slug);
            menu.ApplyManualItem(drink.Id, MenuItemAvailability.Present, 3, null);
            drink.Update(drink.NameRu, drink.NameEn, drink.Category, drink.Aliases, drink.SortOrder, false);
            await db.SaveChangesAsync(Ct);
            Assert.Equal(first.PublicSlug, (await store.SearchShopsAsync(new(), user, Ct)).Items[0].Address.Slug);
        }
        preferred.SetHidden(true); await db.SaveChangesAsync(Ct);
        Assert.Equal(first.PublicSlug, Assert.Single((await store.SearchShopsAsync(new(), user, Ct)).Items).Address.Slug);
    }

    [Fact]
    public async Task LatestPositiveRatingPersonalizes_ButLaterLowRatingAndUnratedVisitsDoNot()
    {
        await using var db = Database();
        var first = Shop(db, "A-first"); var liked = Shop(db, "Z-liked");
        var user = Guid.NewGuid(); var other = Guid.NewGuid();
        Rate(db, first, other, 4, 3); Rate(db, liked, user, 4, 3);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        Assert.Equal(liked.PublicSlug, (await store.SearchShopsAsync(new(), user, Ct)).Items[0].Address.Slug);
        Assert.Equal(first.PublicSlug, (await store.SearchShopsAsync(new(), null, Ct)).Items[0].Address.Slug);
        Rate(db, first, other, 1, 2); Rate(db, liked, user, 1, 2);
        db.CheckIns.Add(CheckIn.CreateUnified(user, liked.Id, "author", null, null, DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync(Ct);
        var result = await store.SearchShopsAsync(new(), user, Ct);
        Assert.Equal(first.PublicSlug, result.Items[0].Address.Slug);
        Assert.True(result.Items[1].IsVisited);
        Assert.Equal(1m, result.Items[1].Rating);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("deleted")]
    [InlineData("rejected")]
    [InlineData("edited")]
    public async Task IneligibleRatingsDoNotAffectQualityOrPersonalization(string state)
    {
        await using var db = Database();
        var first = Shop(db, "A-first"); var preferred = Shop(db, "Z-preferred");
        var user = Guid.NewGuid(); var rating = Rate(db, preferred, user, 5);
        switch (state)
        {
            case "private": rating.ChangeVisibility(CheckInVisibility.Private); break;
            case "deleted": rating.Delete(); break;
            case "rejected": rating.ApplyModerationDecision(1, rating.ModerationSubmissionId!.Value, 2, CheckInModerationState.Rejected, "invalid", DateTime.UtcNow); break;
            case "edited": rating.UpdateContent("changed", Rating.Create(5, 5, 5)); break;
        }
        await db.SaveChangesAsync(Ct);
        var result = await Store(db).SearchShopsAsync(new(), user, Ct);
        Assert.Equal(first.PublicSlug, result.Items[0].Address.Slug);
        Assert.All(result.Items, s => Assert.Equal(0m, s.Rating));
    }

    [Fact]
    public async Task NearbyRecommendationsCanPreferAFavorite_WhileExplicitSortsAndRadiusWin()
    {
        await using var db = Database();
        var nearest = Shop(db, "A-nearest"); var preferred = Shop(db, "Z-preferred");
        preferred.SetLocation(nearest.Location.CityId, "Nearby", 53.901m, 27.56m);
        var user = Guid.NewGuid(); db.DiscoveryFavorites.Add(new(user, "coffee_shop", preferred.Id));
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var request = new CoffeeShopSearchRequest { Filters = new() { Origin = new() { Latitude = 53.9m, Longitude = 27.56m } } };
        Assert.Equal(preferred.PublicSlug, (await store.SearchShopsAsync(request, user, Ct)).Items[0].Address.Slug);
        Assert.Equal(nearest.PublicSlug, (await store.SearchShopsAsync(request, null, Ct)).Items[0].Address.Slug);
        foreach (var sort in new[] { "distance_asc", "name_asc", "rating_desc" })
        {
            request.Sort = sort;
            Assert.Equal(nearest.PublicSlug, (await store.SearchShopsAsync(request, user, Ct)).Items[0].Address.Slug);
        }
        request.Sort = null; request.Filters.RadiusKm = 0.05m;
        Assert.Equal(nearest.PublicSlug, Assert.Single((await store.SearchShopsAsync(request, user, Ct)).Items).Address.Slug);
        request.Filters.MinRating = 4;
        Assert.Empty((await store.SearchShopsAsync(request, user, Ct)).Items);
    }

    [Fact]
    public async Task MissingCoordinatesAreSupported_AndTiesUseSlugBeforePaging()
    {
        await using var db = Database();
        var unknown = Shop(db, "A-unknown"); unknown.SetDraftLocation(unknown.Location.CityId, "Unknown");
        var z = Shop(db, "Same", "z-shop"); var a = Shop(db, "Same", "a-shop");
        var far = Shop(db, "B-far"); far.SetLocation(far.Location.CityId, "Far", 54m, 27.56m);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var request = new CoffeeShopSearchRequest { Filters = new() { Origin = new() { Latitude = 53.9m, Longitude = 27.56m } }, PageSize = 1 };
        var slugs = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            request.Page = page;
            var result = await store.SearchShopsAsync(request, null, Ct);
            Assert.Equal(4, result.TotalItems); Assert.Equal(4, result.TotalPages);
            slugs.Add(Assert.Single(result.Items).Address.Slug);
        }
        Assert.Equal(new[] { a.PublicSlug, z.PublicSlug, far.PublicSlug, unknown.PublicSlug }, slugs);
        request.Page = 5;
        Assert.Empty((await store.SearchShopsAsync(request, null, Ct)).Items);
    }

    [Fact]
    public async Task TextSearchNormalizesYoAndCase_AndRanksNamesAboveAddressesBeforePaging()
    {
        await using var db = Database();
        var exact = Shop(db, "Кофеёк", "exact"); var prefix = Shop(db, "Кофеек центр", "prefix");
        var word = Shop(db, "Уютный кофеёк", "word"); var substring = Shop(db, "Хкофеек", "substring");
        var address = Shop(db, "A-address"); address.SetLocation(address.Location.CityId, "Кофеёк, 10", 53.9m, 27.56m);
        Shop(db, "Кофеек hidden", "hidden").SetHidden(true);
        Shop(db, "Кофеек closed", "closed").SetStatus(CoffeeShopStatus.PermanentlyClosed);
        Shop(db, "Unrelated");
        var user = Guid.NewGuid(); db.DiscoveryFavorites.Add(new(user, "coffee_shop", address.Id));
        await db.SaveChangesAsync(Ct);
        var store = Store(db); var slugs = new List<string>();
        for (var page = 1; page <= 5; page++)
        {
            var result = await store.SearchShopsAsync(new() { Q = "  КОФЕЁК  ", Page = page, PageSize = 1 }, user, Ct);
            Assert.Equal(5, result.TotalItems);
            slugs.Add(Assert.Single(result.Items).Address.Slug);
        }
        Assert.Equal(new[] { exact.PublicSlug, prefix.PublicSlug, word.PublicSlug, substring.PublicSlug, address.PublicSlug }, slugs);
        var explicitRecommendation = await store.SearchShopsAsync(new() { Q = "кофеек", Sort = "recommended" }, user, Ct);
        Assert.Equal(address.PublicSlug, explicitRecommendation.Items[0].Address.Slug);
    }
}
