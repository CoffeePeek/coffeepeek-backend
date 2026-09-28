using System.Reflection;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CoffeePeek.ShopsService.Tests;

public sealed class PublicAddressApiContractTests
{
    private const string AdminRoute = "api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones)$)}/public-addresses";

    [Fact]
    public void PublicRoutes_AreExplicitAndDoNotReplaceGuidDetails()
    {
        Route(typeof(PublicCoffeeShopAddressesController)).Should().Be("api/CoffeeShops");
        Route(typeof(PublicRoasterAddressesController)).Should().Be("api/roasters");
        Template<PublicCoffeeShopAddressesController>(nameof(PublicCoffeeShopAddressesController.GetBySlug))
            .Should().Be("by-slug/{slug}");
        Template<PublicRoasterAddressesController>(nameof(PublicRoasterAddressesController.GetBySlug))
            .Should().Be("by-slug/{slug}");
        Template<PublicCoffeeShopAddressesController>(nameof(PublicCoffeeShopAddressesController.GetBatch))
            .Should().Be("public-addresses");
        Template<PublicRoasterAddressesController>(nameof(PublicRoasterAddressesController.GetBatch))
            .Should().Be("public-addresses");
        Template<CoffeeShopsController>(nameof(CoffeeShopsController.GetCoffeeShop))
            .Should().Be("{id:guid}");
        Template<RoastersController>(nameof(RoastersController.GetById))
            .Should().Be("{id:guid}");

        Route(typeof(PublicCityAddressesController)).Should().Be("api/Catalogs/cities");
        Route(typeof(PublicCoffeeZoneAddressesController)).Should().Be("api/Catalogs/coffee-zones");
        Template<PublicCityAddressesController>(nameof(PublicCityAddressesController.GetBySlug))
            .Should().Be("by-slug/{slug}");
        Template<PublicCoffeeZoneAddressesController>(nameof(PublicCoffeeZoneAddressesController.GetBySlug))
            .Should().Be("by-slug/{slug}");
        Template<PublicCityAddressesController>(nameof(PublicCityAddressesController.GetBatch))
            .Should().Be("public-addresses");
        Template<PublicCoffeeZoneAddressesController>(nameof(PublicCoffeeZoneAddressesController.GetBatch))
            .Should().Be("public-addresses");
        Template<PublicCityAddressesController>(nameof(PublicCityAddressesController.GetById))
            .Should().Be("{id:guid}/public-address");
        Template<PublicCoffeeZoneAddressesController>(nameof(PublicCoffeeZoneAddressesController.GetById))
            .Should().Be("{id:guid}/public-address");

        // The existing city catalog list keeps its route and payload.
        Route(typeof(CatalogsController)).Should().Be("api/[controller]");
        Template<CatalogsController>(nameof(CatalogsController.GetCities)).Should().Be("cities");
    }

    [Fact]
    public void PublicAddressReads_AreNeverResponseCached()
    {
        foreach (var controller in new[]
                 {
                     typeof(PublicCoffeeShopAddressesController), typeof(PublicRoasterAddressesController),
                     typeof(PublicCityAddressesController), typeof(PublicCoffeeZoneAddressesController)
                 })
        {
            var policy = controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
                .Cast<ResponseCacheAttribute>().Single();
            policy.NoStore.Should().BeTrue();
            policy.Location.Should().Be(ResponseCacheLocation.None);
        }
    }

    [Fact]
    public void AddressManagement_RequiresAdminPolicyAndFixedEntityKinds()
    {
        var authorization = typeof(AdminPublicAddressesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();
        authorization.Policy.Should().Be(RoleConsts.Admin);
        Route(typeof(AdminPublicAddressesController)).Should().Be(AdminRoute);
    }

    [Theory]
    [InlineData("shops", PublicAddressKind.CoffeeShop)]
    [InlineData("roasters", PublicAddressKind.Roaster)]
    [InlineData("cities", PublicAddressKind.City)]
    [InlineData("coffee-zones", PublicAddressKind.CoffeeZone)]
    [InlineData("Cities", PublicAddressKind.City)]
    public void AdminEntityKinds_MapExplicitly(string entity, PublicAddressKind expected) =>
        AdminKind(entity).Should().Be(expected);

    [Fact]
    public void AdminRouteEntities_CoverEveryAddressKind()
    {
        var route = Route(typeof(AdminPublicAddressesController))!;
        var start = route.IndexOf("^(", StringComparison.Ordinal) + 2;
        var end = route.IndexOf(")$", start, StringComparison.Ordinal);
        var entities = route[start..end].Split('|');

        entities.Select(AdminKind).Should().BeEquivalentTo(Enum.GetValues<PublicAddressKind>());

        // Anything outside the route regex is rejected instead of defaulting to a kind.
        var unknown = () => AdminKind("equipments");
        unknown.Should().Throw<TargetInvocationException>()
            .WithInnerException<PublicAddressException>()
            .Which.StatusCode.Should().Be(400);
    }

    private static PublicAddressKind AdminKind(string entity) => (PublicAddressKind)typeof(AdminPublicAddressesController)
        .GetMethod("Kind", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [entity])!;

    private static string? Route(Type controller) => controller
        .GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template;

    private static string? Template<T>(string method) => typeof(T).GetMethod(method)!
        .GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single().Template;
}
