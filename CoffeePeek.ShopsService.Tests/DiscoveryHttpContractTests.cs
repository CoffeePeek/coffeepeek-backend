using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.ShopsService;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class DiscoveryHttpContractTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private const string Secret = "discovery-http-tests-private-gateway-secret-123456789";
    private readonly Mock<IDiscoveryQueryStore> queries = new();
    private readonly Mock<IPublicCoffeeCatalogReader> catalog = new();
    private readonly Mock<IDiscoveryClassificationStore> classification = new();
    private readonly Mock<IDiscoveryFavoritesStore> favorites = new();
    private readonly Mock<IMessageBus> bus = new();
    private readonly Mock<IProfileAddressLookup> profiles = new();
    private readonly Mock<ICoffeeShopQueries> legacyShops = new();
    private readonly Mock<IPublicAddressService> addresses = new();
    private readonly Mock<IQueryCheckInRepository> checkIns = new();

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(DiscoveryController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayAuth:SecretKey"] = Secret });
        builder.Services.AddPresentation(builder.Configuration);
        builder.Services.AddSingleton(queries.Object);
        builder.Services.AddSingleton(classification.Object);
        builder.Services.AddSingleton(favorites.Object);
        builder.Services.AddSingleton(bus.Object);
        builder.Services.AddSingleton(profiles.Object);
        builder.Services.AddSingleton(catalog.Object);
        builder.Services.AddSingleton(legacyShops.Object);
        builder.Services.AddSingleton(addresses.Object);
        builder.Services.AddSingleton(checkIns.Object);
        builder.Services.AddSingleton(Mock.Of<IPublicCheckInProjector>());
        builder.Services.AddScoped<PublicDiscoveryService>();
        addresses.Setup(a => a.GetForReadAsync(It.IsAny<PublicAddressKind>(), It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        bus.Setup(b => b.InvokeAsync(It.IsAny<SetDiscoveryFavoriteCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) =>
                SetDiscoveryFavoriteHandler.Handle((SetDiscoveryFavoriteCommand)command, favorites.Object, ct));
        bus.Setup(b => b.InvokeAsync<AdminCoffeeFilterValue>(It.IsAny<CreateCoffeeFilterValueCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) => CoffeeClassificationHandler.Handle((CreateCoffeeFilterValueCommand)command, classification.Object, ct));
        bus.Setup(b => b.InvokeAsync<AdminCoffeeFilterValue?>(It.IsAny<EditCoffeeFilterValueCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) => CoffeeClassificationHandler.Handle((EditCoffeeFilterValueCommand)command, classification.Object, ct));
        bus.Setup(b => b.InvokeAsync<AdminCoffeeFilterValue?>(It.IsAny<DeactivateCoffeeFilterValueCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) => CoffeeClassificationHandler.Handle((DeactivateCoffeeFilterValueCommand)command, classification.Object, ct));
        bus.Setup(b => b.InvokeAsync<AdminCoffeeClassification?>(It.IsAny<EditCoffeeClassificationCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) => CoffeeClassificationHandler.Handle((EditCoffeeClassificationCommand)command, classification.Object, ct));
        bus.Setup(b => b.InvokeAsync<AdminCoffeeClassification?>(It.IsAny<EditCoffeeVariantClassificationCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken ct, TimeSpan? _) => CoffeeClassificationHandler.Handle((EditCoffeeVariantClassificationCommand)command, classification.Object, ct));
        queries.Setup(q => q.SearchCoffeesAsync(It.IsAny<CoffeeSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeePage { CurrentPage = 1, PageSize = 20 });
        classification.Setup(c => c.EditClassificationAsync(It.IsAny<Guid>(), It.IsAny<CoffeeClassificationEditRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DiscoveryException("Coffee changed; reload.", 409, "VERSION_CONFLICT"));
        app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication(); app.UseAuthorization(); app.UseResponseCaching(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }

    [Theory]
    [InlineData("/api/v1/coffee-shops/search", "{\"filters\":{\"budget\":{\"currency\":\"BYN\",\"maxDrinkPrice\":25}}}")]
    [InlineData("/api/v1/coffees/search", "{\"sort\":\"price_asc\"}")]
    [InlineData("/api/v1/coffees/search", "{\"sort\":\"relevance\",\"q\":\"  \"}")]
    [InlineData("/api/v1/coffees/search", "{\"pageSize\":101}")]
    [InlineData("/api/v1/coffee-shops/search", "{\"sort\":\"unknown\"}")]
    [InlineData("/api/v1/coffee-shops/search", "{\"sort\":\"recommended\",\"userId\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task InvalidRequests_ReturnDocumentedErrorBeforeExecutingSearch(string route, string json)
    {
        using var response = await Post(route, json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_FILTER", body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("errors").ValueKind);
        queries.Verify(q => q.SearchCoffeesAsync(It.IsAny<CoffeeSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        queries.Verify(q => q.SearchShopsAsync(It.IsAny<CoffeeShopSearchRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FullRoasterList_HasMergedFieldsAndNoPaging_AndOldSearchRoutesAreRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        var tag = new PublicTagDto("wholesale", "Wholesale", null, 0);
        var rows = Enumerable.Range(0, 105).Select(i => new PublicRoasterDto(
            new($"roast-{i}", $"/roasters/roast-{i}", 1), $"Roast {i}", "https://media.example/roast.jpg",
            null, [tag], 2, 3, 1)).ToArray();
        catalog.Setup(c => c.RoastersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        foreach (var route in new[] { "/api/roasters", "/api/roasters?tags=wholesale&page=2&pageSize=1" })
        {
            using var response = await client.GetAsync(route, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = body.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Array, data.ValueKind); Assert.Equal(105, data.GetArrayLength());
            var item = data[0];
            Assert.Equal("roast-0", item.GetProperty("address").GetProperty("slug").GetString());
            Assert.Equal("Roast 0", item.GetProperty("name").GetString());
            Assert.Equal("https://media.example/roast.jpg", item.GetProperty("photoUrl").GetString());
            Assert.True(item.TryGetProperty("coverPhoto", out _));
            Assert.Equal("wholesale", item.GetProperty("tags")[0].GetProperty("slug").GetString());
            Assert.Equal(2, item.GetProperty("coffeeShopsCount").GetInt32());
            Assert.Equal(3, item.GetProperty("coffeeProductsCount").GetInt32());
            Assert.Equal(1, item.GetProperty("availableCoffeeProducts").GetInt32());
            Assert.False(item.TryGetProperty("catalogUpdatedAtUtc", out _));
            Assert.False(item.TryGetProperty("coffeeCatalogUpdatedAtUtc", out _));
        }
        catalog.Verify(c => c.RoastersAsync(It.Is<string[]>(t => t.Length == 0), It.IsAny<CancellationToken>()), Times.Once);
        catalog.Verify(c => c.RoastersAsync(It.Is<string[]>(t => t.SequenceEqual(new[] { "wholesale" })), It.IsAny<CancellationToken>()), Times.Once);
        using var legacy = await client.GetAsync("/api/Catalogs/roasters", ct);
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
        foreach (var route in new[] { "/api/v1/roasters/search", "/api/v1/roasters/facets" })
        {
            using var response = await Post(route, "{}"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task RemovedDiscoverySearch_ReturnsNotFound()
    {
        using var response = await Post("/api/v1/discovery/search", "{}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        queries.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyPicker_PreservesSelectionResponsePagingAndVisitedState(bool authenticated)
    {
        var userId = Guid.NewGuid(); var cityId = Guid.NewGuid(); var shopId = Guid.NewGuid();
        var city = new PublicAddressMetadata(cityId, "minsk", "/cities/minsk", 1);
        var shop = new PublicAddressMetadata(shopId, "example-coffee", "/coffee-shops/example-coffee", 2);
        if (authenticated) Authenticate(userId, RoleConsts.User);
        addresses.Setup(a => a.ResolveAsync(PublicAddressKind.City, "minsk", It.IsAny<CancellationToken>())).ReturnsAsync(city);
        addresses.Setup(a => a.GetForReadAsync(PublicAddressKind.City, It.IsAny<Guid[]>(), It.IsAny<CancellationToken>())).ReturnsAsync([city]);
        addresses.Setup(a => a.GetForReadAsync(PublicAddressKind.CoffeeShop, It.IsAny<Guid[]>(), It.IsAny<CancellationToken>())).ReturnsAsync([shop]);
        checkIns.Setup(c => c.GetVisitedShopIdsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([shopId]);
        legacyShops.Setup(s => s.Search(It.IsAny<SearchCoffeeShopsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { new ShortShopDto { Id = shopId, CityId = cityId, Name = "Example coffee", Photos = [], Rating = 4.5m } }, 3));

        using var response = await client.GetAsync("/api/CoffeeShops?city=minsk&q=Street&page=2&pageSize=2", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("@1792972800", Assert.Single(response.Headers.GetValues("Deprecation")));
        Assert.Equal("</api/v1/coffee-shops/search>; rel=\"successor-version\"", Assert.Single(response.Headers.GetValues("Link")));
        Assert.Equal("3", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        foreach (var header in new[] { "X-Total-Pages", "X-Current-Page", "X-Page-Size" })
            Assert.Equal("2", Assert.Single(response.Headers.GetValues(header)));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(body.RootElement.GetProperty("isSuccess").GetBoolean());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(data.GetProperty("coffeeShops").EnumerateArray());
        Assert.Equal("example-coffee", item.GetProperty("address").GetProperty("slug").GetString());
        Assert.Equal("/coffee-shops/example-coffee", item.GetProperty("address").GetProperty("canonicalPath").GetString());
        Assert.Equal(2, item.GetProperty("address").GetProperty("revision").GetInt32());
        Assert.Equal("minsk", item.GetProperty("city").GetProperty("slug").GetString());
        Assert.Equal("Example coffee", item.GetProperty("name").GetString());
        Assert.Equal(4.5m, item.GetProperty("rating").GetDecimal());
        Assert.Equal(authenticated, item.GetProperty("isVisited").GetBoolean());
        Assert.False(item.TryGetProperty("id", out _));
        var expected = new SearchCoffeeShopsQuery(
            UserId: authenticated ? userId : null, Query: "Street", CityId: cityId, PageNumber: 2, PageSize: 2);
        legacyShops.Verify(s => s.Search(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("roasters")]
    [InlineData("equipments")]
    [InlineData("beans")]
    [InlineData("brewMethods")]
    [InlineData("tags")]
    [InlineData("isOpen")]
    [InlineData("isNew")]
    [InlineData("isVisited")]
    [InlineData("priceRange")]
    [InlineData("type")]
    [InlineData("minRating")]
    public async Task LegacyPicker_RejectsRemovedFiltersBeforeReadingShops(string parameter)
    {
        using var response = await client.GetAsync($"/api/CoffeeShops?{parameter}=anything", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("INVALID_FILTER", body.RootElement.GetProperty("errorCode").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(parameter, out _));
        legacyShops.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=10001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task LegacyPicker_ValidatesPagingBeforeReadingShops(string query)
    {
        using var response = await client.GetAsync($"/api/CoffeeShops?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        legacyShops.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyPicker_RejectsUnknownCityInsteadOfReturningAllShops()
    {
        using var response = await client.GetAsync("/api/CoffeeShops?city=unknown", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        legacyShops.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicSearchAllowsGuests_WhilePersonalFiltersAndFavoritesRequireAuthentication()
    {
        using var search = await Post("/api/v1/coffees/search", "{}");
        Assert.True(search.StatusCode == HttpStatusCode.OK, await search.Content.ReadAsStringAsync());
        using var personal = await Post("/api/v1/coffee-shops/search", "{\"filters\":{\"favoritesOnly\":true}}");
        Assert.Equal(HttpStatusCode.Unauthorized, personal.StatusCode);
        using var favorite = await client.PutAsync("/api/v1/favorites/roaster/sample-roaster", null);
        Assert.Equal(HttpStatusCode.Unauthorized, favorite.StatusCode);
        favorites.Verify(f => f.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(b => b.InvokeAsync(It.IsAny<SetDiscoveryFavoriteCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Theory]
    [InlineData("/api/coffees?search=one", "/api/coffees?search=two")]
    [InlineData("/api/roasters/example/coffees?search=one", "/api/roasters/example/coffees?search=two")]
    public async Task CoffeeLists_CacheSuccessfulResponsesByQuery(string firstUrl, string secondUrl)
    {
        catalog.Setup(c => c.ListAsync(It.IsAny<CoffeeCatalogFilter>(), It.IsAny<CancellationToken>()))
            .Returns((CoffeeCatalogFilter filter, CancellationToken _) => Task.FromResult(
                new PublicPage<CoffeeProductDto>([], filter.Search == "one" ? 1 : 2, 1, 1, 20)));

        using var first = await client.GetAsync(firstUrl, TestContext.Current.CancellationToken);
        using var repeated = await client.GetAsync(firstUrl, TestContext.Current.CancellationToken);
        using var second = await client.GetAsync(secondUrl, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(first.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(2), first.Headers.CacheControl?.MaxAge);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("data").GetProperty("totalItems").GetInt32());
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("data").GetProperty("totalItems").GetInt32());
        catalog.Verify(c => c.ListAsync(It.IsAny<CoffeeCatalogFilter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CoffeeDetails_CacheOnlySuccessfulResponses()
    {
        queries.Setup(q => q.CoffeeDetailsAsync("published", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeeDetails { Name = "Published coffee" });
        catalog.Setup(c => c.GetAsync("published", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeeProductDto(new PublicAddressDto("published", "/coffees/published", 1),
                new PublicRoasterSummaryDto(null, "Roaster", null, null),
                new CoffeeContentDto("Published coffee", null, "roasted_beans", "whole_beans", "single_origin",
                    null, null, null, null, null, [], [], [], []), [], [], [], null));

        using var published = await client.GetAsync("/api/v1/coffees/published", TestContext.Current.CancellationToken);
        using var missing = await client.GetAsync("/api/v1/coffees/missing", TestContext.Current.CancellationToken);
        using var legacyPublished = await client.GetAsync("/api/coffees/published", TestContext.Current.CancellationToken);
        using var legacyMissing = await client.GetAsync("/api/coffees/missing", TestContext.Current.CancellationToken);
        using var search = await client.PostAsync("/api/v1/coffees/search", Content("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.True(published.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(2), published.Headers.CacheControl?.MaxAge);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True(missing.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, legacyPublished.StatusCode);
        Assert.True(legacyPublished.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(2), legacyPublished.Headers.CacheControl?.MaxAge);
        Assert.Equal(HttpStatusCode.NotFound, legacyMissing.StatusCode);
        Assert.True(legacyMissing.Headers.CacheControl?.NoStore);
        Assert.True(search.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("recommended", false)]
    [InlineData("recommended", true)]
    public async Task ShopRecommendations_AcceptGuestsAndUseOnlyAuthenticatedIdentity(string? sort, bool authenticated)
    {
        var ct = TestContext.Current.CancellationToken;
        Guid? userId = authenticated ? Guid.NewGuid() : null;
        if (userId.HasValue) Authenticate(userId.Value, RoleConsts.User);
        queries.Setup(q => q.SearchShopsAsync(It.IsAny<CoffeeShopSearchRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeeShopPage { CurrentPage = 1, PageSize = 20 });
        using var response = await client.PostAsJsonAsync("/api/v1/coffee-shops/search", new CoffeeShopSearchRequest { Sort = sort }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("data").GetProperty("items").ValueKind);
        queries.Verify(q => q.SearchShopsAsync(It.Is<CoffeeShopSearchRequest>(r => r.Sort == sort), userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidPriceSearchAcceptsIntegerWeightAndDecimalPrice()
    {
        using var response = await Post("/api/v1/coffees/search",
            "{\"page\":2,\"pageSize\":10,\"sort\":\"price_asc\",\"filters\":{\"weightGrams\":250,\"currency\":\"BYN\",\"maxPrice\":25.5,\"brew\":[\"filter\"]}}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        queries.Verify(q => q.SearchCoffeesAsync(It.Is<CoffeeSearchRequest>(r => r.Page == 2 && r.PageSize == 10 &&
            r.Filters!.WeightGrams == 250 && r.Filters.MaxPrice == 25.5m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FavoriteOwnerIsTakenFromAuthenticatedPrincipal(bool favorite)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid(); Authenticate(id, RoleConsts.User);
        favorites.Setup(f => f.SetAsync(id, "roaster", "sample-roaster", favorite, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var response = favorite
            ? await client.PutAsync("/api/v1/favorites/roaster/sample-roaster", null, ct)
            : await client.DeleteAsync("/api/v1/favorites/roaster/sample-roaster", ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        bus.Verify(b => b.InvokeAsync(It.Is<SetDiscoveryFavoriteCommand>(c => c.UserId == id && c.Kind == "roaster" &&
            c.Slug == "sample-roaster" && c.Favorite == favorite), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
        favorites.Verify(f => f.SetAsync(id, "roaster", "sample-roaster", favorite, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, "INVALID_FILTER")]
    [InlineData(404, "NOT_FOUND")]
    public async Task FavoriteCommandFailure_PreservesHttpErrorContract(int status, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        Authenticate(Guid.NewGuid(), RoleConsts.User);
        favorites.Setup(f => f.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DiscoveryException("Favorite rejected.", status, code));
        using var response = await client.PutAsync("/api/v1/favorites/roaster/sample-roaster", null, ct);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(code, body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
    }

    [Fact]
    public async Task PublicFavorites_ResolveRequestedProfile_ForGuestsAndAuthenticatedViewers()
    {
        var author = Guid.NewGuid();
        profiles.Setup(p => p.ResolveAsync("glasha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(author, "glasha", "/users/glasha", 1));
        favorites.Setup(f => f.ListAsync(author, It.IsAny<CancellationToken>())).ReturnsAsync([
            new DiscoveryFavoriteItem { Kind = "drink", Drink = new() { Slug = "matcha_latte" }, Name = "Матча латте", CreatedAtUtc = DateTime.UtcNow }
        ]);
        foreach (var signedIn in new[] { false, true })
        {
            if (signedIn) Authenticate(Guid.NewGuid(), RoleConsts.User);
            using var response = await client.GetAsync("/api/v1/profiles/glasha/favorites", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var item = body.RootElement.GetProperty("data")[0];
            Assert.Equal("Матча латте", item.GetProperty("name").GetString());
            Assert.Equal("matcha_latte", item.GetProperty("drink").GetProperty("slug").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("address").ValueKind);
            Assert.False(item.TryGetProperty("userId", out _));
        }
        favorites.Verify(f => f.ListAsync(author, It.IsAny<CancellationToken>()), Times.Exactly(2));
        favorites.Verify(f => f.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden-profile")]
    public async Task UnavailableProfileFavorites_Return404WithoutReadingFavorites(string slug)
    {
        profiles.Setup(p => p.ResolveAsync(slug, It.IsAny<CancellationToken>())).ReturnsAsync((PublicAddressMetadata?)null);
        using var response = await client.GetAsync($"/api/v1/profiles/{slug}/favorites", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        favorites.Verify(f => f.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("/api/v1/favorites")]
    [InlineData("/api/v1/profiles/glasha/favorites", true)]
    public async Task FavoriteMutations_RequireAuthenticationOrHaveNoPublicRoute(string route, bool publicRoute = false)
    {
        using var response = publicRoute
            ? await client.PutAsync(route, null, TestContext.Current.CancellationToken)
            : await client.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(publicRoute ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.Unauthorized, response.StatusCode);
        favorites.Verify(f => f.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdministrativeClassificationRequiresRole_AndReturnsVersionConflict()
    {
        var route = $"/api/admin/coffees/{Guid.NewGuid()}/classification";
        var json = "{\"version\":1,\"protectedFields\":[],\"classification\":{\"defaultBrewPurposes\":[],\"caffeine\":null,\"roastLevel\":null,\"acidity\":null,\"processing\":[],\"fermentation\":[],\"tasteGroups\":[],\"composition\":null}}";
        Authenticate(Guid.NewGuid(), RoleConsts.User);
        using var denied = await client.PatchAsync(route, Content(json)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Authenticate(Guid.NewGuid(), RoleConsts.Moderator);
        using var conflict = await client.PatchAsync(route, Content(json));
        Assert.True(conflict.StatusCode == HttpStatusCode.Conflict, await conflict.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("VERSION_CONFLICT", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("create", true)]
    [InlineData("edit", true)]
    [InlineData("edit", false)]
    [InlineData("deactivate", true)]
    [InlineData("deactivate", false)]
    [InlineData("product", true)]
    [InlineData("product", false)]
    [InlineData("variant", true)]
    [InlineData("variant", false)]
    public async Task ClassificationCommands_PreserveSuccessAndNotFound(string operation, bool found)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid(); var variantId = Guid.NewGuid();
        var value = found ? new AdminCoffeeFilterValue { Id = id, GroupCode = "taste", Code = "berry", Name = "Berry" } : null;
        var product = found ? new AdminCoffeeClassification { Id = id, Version = 2 } : null;
        classification.Setup(s => s.CreateValueAsync(It.IsAny<CreateCoffeeFilterValueRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(value!);
        classification.Setup(s => s.EditValueAsync(id, It.IsAny<EditCoffeeFilterValueRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(value);
        classification.Setup(s => s.DeactivateValueAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(value);
        classification.Setup(s => s.EditClassificationAsync(id, It.IsAny<CoffeeClassificationEditRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(product);
        classification.Setup(s => s.EditVariantClassificationAsync(id, variantId, It.IsAny<VariantClassificationEditRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(product);
        Authenticate(Guid.NewGuid(), RoleConsts.Moderator);
        using var response = operation switch
        {
            "create" => await client.PostAsJsonAsync("/api/admin/coffee-filter-values", new CreateCoffeeFilterValueRequest
                { GroupCode = "taste", Code = "berry", Name = "Berry" }, ct),
            "edit" => await client.PatchAsJsonAsync($"/api/admin/coffee-filter-values/{id}", new EditCoffeeFilterValueRequest
                { Name = "Berry", IsActive = true }, ct),
            "deactivate" => await client.DeleteAsync($"/api/admin/coffee-filter-values/{id}", ct),
            "product" => await client.PatchAsJsonAsync($"/api/admin/coffees/{id}/classification", new CoffeeClassificationEditRequest
                { Version = 1, Classification = new(), ProtectedFields = [] }, ct),
            "variant" => await client.PatchAsJsonAsync($"/api/admin/coffees/{id}/variants/{variantId}/classification", new VariantClassificationEditRequest
                { Version = 1, BrewPurpose = "filter" }, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Assert.Equal(found ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (found) Assert.True(body.RootElement.GetProperty("isSuccess").GetBoolean());
        else Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("errorCode").GetString());
    }

    private const string ValidMap = """{"q":"","filters":{},"bounds":{"minLat":53.8,"minLon":27.4,"maxLat":54,"maxLon":27.7},"zoom":16}""";

    [Theory]
    [InlineData("\"zoom\":16", "\"zoom\":23")]
    [InlineData("\"zoom\":16", "\"zoom\":-1")]
    [InlineData(",\"zoom\":16", "")]
    [InlineData("\"minLat\":53.8", "\"minLat\":-91")]
    [InlineData("\"maxLon\":27.7", "\"maxLon\":181")]
    [InlineData("\"minLat\":53.8", "\"minLat\":55")]
    [InlineData("\"minLon\":27.4", "\"minLon\":28")]
    [InlineData("\"minLat\":53.8,", "")]
    [InlineData("\"maxLat\":54,", "")]
    [InlineData("\"zoom\":16", "\"zoom\":16,\"userId\":\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("\"zoom\":16", "\"zoom\":16,\"sort\":\"name_asc\"")]
    [InlineData("\"minLat\":53.8", "\"minLat\":53.8,\"unknown\":1")]
    [InlineData("\"filters\":{}", "\"filters\":{\"unknown\":true}")]
    [InlineData("\"filters\":{}", "\"filters\":{\"radiusKm\":1}")]
    [InlineData("\"filters\":{}", "\"filters\":{\"priceRange\":\"unknown\"}")]
    [InlineData("\"filters\":{}", "\"filters\":{\"menu\":{\"maxPrice\":5}}")]
    public async Task InvalidMapRequests_ReturnInvalidFilterWithoutReading(string before, string after)
    {
        using var response = await Post("/api/v1/coffee-shops/map", ValidMap.Replace(before, after));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_FILTER", body.RootElement.GetProperty("errorCode").GetString());
        queries.Verify(q => q.MapShopsAsync(It.IsAny<CoffeeShopMapRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"zoom\":16}")]
    [InlineData("{\"bounds\":null,\"zoom\":16}")]
    public async Task MapRequiresExplicitBounds(string json)
    {
        using var response = await Post("/api/v1/coffee-shops/map", json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        queries.Verify(q => q.MapShopsAsync(It.IsAny<CoffeeShopMapRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("favoritesOnly")]
    [InlineData("visitedOnly")]
    public async Task MapPersonalFilters_RequireGatewayAuthenticatedIdentity(string filter)
    {
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, Guid.NewGuid().ToString());
        using var response = await Post("/api/v1/coffee-shops/map", ValidMap.Replace("\"filters\":{}", $"\"filters\":{{\"{filter}\":true}}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        queries.Verify(q => q.MapShopsAsync(It.IsAny<CoffeeShopMapRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapRoutes_PreservePublicResponseAndPassAuthenticatedIdentityOnly(bool authenticated)
    {
        var shopId = Guid.NewGuid();
        var userId = authenticated ? Guid.NewGuid() : (Guid?)null;
        if (userId.HasValue) Authenticate(userId.Value, RoleConsts.User);
        var result = new GetShopsInBoundsResponse([new() { Id = shopId, Latitude = 53.9m, Longitude = 27.56m, Title = "Map shop" }])
            { Clusters = [], Zones = [], IsTruncated = false };
        queries.Setup(q => q.MapShopsAsync(It.IsAny<CoffeeShopMapRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        bus.Setup(b => b.InvokeAsync<Response<GetShopsInBoundsResponse>>(It.IsAny<GetShopsInBoundsQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetShopsInBoundsResponse>.Success(result));
        addresses.Setup(a => a.GetForReadAsync(PublicAddressKind.CoffeeShop, It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(shopId, "map-shop", "/coffee-shops/map-shop", 1)]);
        var json = ValidMap.Replace("\"q\":\"\"", "\"q\":\"Map\"")
            .Replace("\"filters\":{}", "\"filters\":{\"tags\":[\"laptop-friendly\"],\"favoritesOnly\":" + authenticated.ToString().ToLowerInvariant() + "}");
        using var map = await Post("/api/v1/coffee-shops/map", json);
        using var legacy = await client.GetAsync("/api/Map?minLat=53.8&minLon=27.4&maxLat=54&maxLon=27.7", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode); Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        using var body = JsonDocument.Parse(await map.Content.ReadAsStringAsync());
        using var oldBody = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(oldBody.RootElement.GetProperty("data").GetRawText(), data.GetRawText());
        Assert.Equal("map-shop", Assert.Single(data.GetProperty("shops").EnumerateArray()).GetProperty("address").GetProperty("slug").GetString());
        Assert.False(data.GetProperty("isTruncated").GetBoolean());
        queries.Verify(q => q.MapShopsAsync(It.Is<CoffeeShopMapRequest>(r => r.Q == "Map" && r.Zoom == 16 &&
            r.Bounds.MinLat == 53.8m && r.Bounds.MaxLon == 27.7m && r.Filters!.Tags.Single() == "laptop-friendly" &&
            r.Filters.FavoritesOnly == authenticated), userId, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.InvokeAsync<Response<GetShopsInBoundsResponse>>(It.Is<GetShopsInBoundsQuery>(q => q.Zoom == null),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task MapUnknownSlug_PreservesDocumentedValidationError()
    {
        queries.Setup(q => q.MapShopsAsync(It.IsAny<CoffeeShopMapRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DiscoveryException("Unknown public slug.", errors: new() { ["filters.tags"] = ["Unknown public slug."] }));
        using var response = await Post("/api/v1/coffee-shops/map", ValidMap.Replace("\"filters\":{}", "\"filters\":{\"tags\":[\"missing\"]}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_FILTER", body.RootElement.GetProperty("errorCode").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("filters.tags", out _));
    }

    private void Authenticate(Guid id, string role)
    {
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, Secret);
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, id.ToString());
        client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserRole, role);
    }
    private Task<HttpResponseMessage> Post(string route, string json) => client.PostAsync(route, Content(json));
    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/json");
}
