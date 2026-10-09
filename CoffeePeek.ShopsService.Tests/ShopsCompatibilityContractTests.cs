using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CoffeePeek.ShopsService.Tests;

public class ShopsCompatibilityContractTests
{
    [Theory]
    [InlineData(typeof(CoffeeShopsController), nameof(CoffeeShopsController.GetCoffeeShops), "GET", "api/CoffeeShops")]
    [InlineData(typeof(CoffeeShopsController), nameof(CoffeeShopsController.GetCoffeeShop), "GET", "api/CoffeeShops/{slug}")]
    [InlineData(typeof(CoffeeShopsController), nameof(CoffeeShopsController.GetCoffeeShop), "GET", "api/CoffeeShops/by-slug/{slug}")]
    [InlineData(typeof(CatalogsController), nameof(CatalogsController.GetCities), "GET", "api/Catalogs/cities")]
    [InlineData(typeof(CatalogsController), nameof(CatalogsController.GetBeans), "GET", "api/Catalogs/beans")]
    [InlineData(typeof(CatalogsController), nameof(CatalogsController.GetEquipment), "GET", "api/Catalogs/equipments")]
    [InlineData(typeof(CatalogsController), nameof(CatalogsController.GetBrewMethods), "GET", "api/Catalogs/brew-methods")]
    [InlineData(typeof(CatalogsController), nameof(CatalogsController.GetShopTags), "GET", "api/Catalogs/shop-tags")]
    [InlineData(typeof(DiscoveryController), nameof(DiscoveryController.Discovery), "POST", "api/v1/discovery/search")]
    [InlineData(typeof(DiscoveryController), nameof(DiscoveryController.Shops), "POST", "api/v1/coffee-shops/search")]
    [InlineData(typeof(AdminCoffeeShopsController), nameof(AdminCoffeeShopsController.UpdateShop), "PUT", "api/admin/shops/{id:guid}")]
    [InlineData(typeof(OwnerCoffeeShopsController), nameof(OwnerCoffeeShopsController.UpdateMyShop), "PUT", "api/owner/coffee-shops/{id:guid}")]
    [InlineData(typeof(AdminMenuOcrRecoveryController), nameof(AdminMenuOcrRecoveryController.RecoverUnknownOutcome), "POST", "api/admin/menu-ocr-jobs/{jobId:guid}/recover-unknown-outcome")]
    [InlineData(typeof(InternalCoffeeImportController), nameof(InternalCoffeeImportController.Import), "POST", "internal/coffee-import/snapshots")]
    [InlineData(typeof(InternalCoffeeImportController), nameof(InternalCoffeeImportController.Failure), "POST", "internal/coffee-import/failures")]
    public void PublishedControllerRouteAndVerbRemainStable(Type controller, string action, string verb, string expected)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
        prefix = prefix.Replace("[controller]", controller.Name.Replace("Controller", ""), StringComparison.Ordinal);
        var routes = controller.GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Where(a => a.HttpMethods.Contains(verb, StringComparer.Ordinal))
            .Select(a => string.Join('/', new[] { prefix, a.Template }.Where(s => !string.IsNullOrEmpty(s))).Trim('/'));

        Assert.Contains(expected, routes);
    }

    [Theory]
    [InlineData("/api/CoffeeShops/{**remainder}", "shops-cluster")]
    [InlineData("/api/Catalogs/{**remainder}", "shops-cluster")]
    [InlineData("/api/v1/discovery/search", "shops-cluster")]
    [InlineData("/api/admin/shops/{**remainder}", "shops-cluster")]
    [InlineData("/api/admin/menu-ocr-jobs/{**remainder}", "shops-cluster")]
    [InlineData("/api/owner/{**remainder}", "shops-cluster")]
    [InlineData("/api/admin/{**remainder}", "account-cluster")]
    public void GatewayKeepsServiceOwnershipForEstablishedRoutes(string path, string cluster)
    {
        var gatewayConfig = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../CoffeePeek.Gateway/appsettings.json"));
        using var json = JsonDocument.Parse(File.ReadAllText(gatewayConfig));
        var routes = json.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var matching = routes.EnumerateObject()
            .Where(route => route.Value.GetProperty("Match").GetProperty("Path").GetString() == path)
            .ToArray();

        Assert.Contains(matching, route => route.Value.GetProperty("ClusterId").GetString() == cluster);
    }

    [Fact]
    public void PublishedEventClrIdentitiesRemainStable()
    {
        Assert.Equal("CoffeePeek.Contract.Events.Moderation.ModerationShopApprovedEvent", typeof(ModerationShopApprovedEvent).FullName);
        Assert.Equal("CoffeePeek.Contract.Events.Moderation.ShopChangeRequestApprovedV1", typeof(ShopChangeRequestApprovedV1).FullName);
        Assert.Equal("CoffeePeek.Contract.Events.Menu.ParseMenuRequestedEvent", typeof(ParseMenuRequestedEvent).FullName);
        Assert.Equal("CoffeePeek.Contract.Events.Shops.CheckinCreatedEvent", typeof(CheckinCreatedEvent).FullName);
    }

    [Fact]
    public void ConventionalRoutingHandlerIdentitiesRemainStable()
    {
        Assert.Equal("CoffeePeek.Shops.Infrastructure.Consumers.ModerationShopApproveHandler", typeof(ModerationShopApproveHandler).FullName);
        Assert.Equal("CoffeePeek.Shops.Infrastructure.Consumers.ShopChangeRequestApprovedHandler", typeof(ShopChangeRequestApprovedHandler).FullName);
        Assert.Equal("CoffeePeek.Shops.Infrastructure.Consumers.ParseMenuRequestedEventHandler", typeof(ParseMenuRequestedEventHandler).FullName);
        Assert.Equal("CoffeePeek.Shops.Infrastructure.Consumers.UserNameChangedHandler", typeof(UserNameChangedHandler).FullName);
    }

    [Fact]
    public void LegacyMenuParseJsonWithoutJobFieldsStillDeserializes()
    {
        const string oldMessage = """
            {"sourceKind":1,"sourceId":"11111111-1111-4111-8111-111111111111","publishedShopId":null,"photos":[],"requestedByUserId":null}
            """;
        var message = JsonSerializer.Deserialize<ParseMenuRequestedEvent>(oldMessage,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(message);
        Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), message.SourceId);
        Assert.Empty(message.Photos);
        Assert.Null(message.JobId);
        Assert.Null(message.MenuRevision);
        Assert.Null(message.PhotoSetHash);
    }

    [Fact]
    public void PublicShopSummaryRemainsAddressBasedInJson()
    {
        var summary = new PublicShopSummaryDto
        {
            Address = new PublicAddressDto("sample-shop", "/coffee-shops/sample-shop", 3),
            Name = "Sample"
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(summary,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal("sample-shop", json.RootElement.GetProperty("address").GetProperty("slug").GetString());
        Assert.Equal("/coffee-shops/sample-shop", json.RootElement.GetProperty("address").GetProperty("canonicalPath").GetString());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("beans").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("id", out _));
        Assert.False(json.RootElement.TryGetProperty("shopId", out _));
    }
}
