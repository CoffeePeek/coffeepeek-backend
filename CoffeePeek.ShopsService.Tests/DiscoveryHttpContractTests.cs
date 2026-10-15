using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
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
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }

    [Theory]
    [InlineData("/api/v1/discovery/search", "{\"filters\":{\"city\":\"minsk\"}}")]
    [InlineData("/api/v1/discovery/search", "{\"filters\":{\"budget\":{\"currency\":\"BYN\",\"maxCoffeePrice\":25}}}")]
    [InlineData("/api/v1/coffees/search", "{\"sort\":\"price_asc\"}")]
    [InlineData("/api/v1/coffees/search", "{\"sort\":\"relevance\",\"q\":\"  \"}")]
    [InlineData("/api/v1/coffees/search", "{\"pageSize\":101}")]
    public async Task InvalidRequests_ReturnDocumentedErrorBeforeExecutingSearch(string route, string json)
    {
        using var response = await Post(route, json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_FILTER", body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("errors").ValueKind);
        queries.Verify(q => q.SearchCoffeesAsync(It.IsAny<CoffeeSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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
        using var shared = await Post("/api/v1/discovery/search", "{\"sections\":[\"roasters\"]}");
        Assert.Equal(HttpStatusCode.BadRequest, shared.StatusCode);
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
