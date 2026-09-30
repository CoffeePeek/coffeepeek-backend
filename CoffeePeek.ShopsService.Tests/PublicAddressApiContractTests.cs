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
    private const string AdminRoute = "api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones|beans|equipments|brew-methods|shop-tags)$)}/public-addresses";

    [Fact]
    public void PublicRoutes_UseSlugsAndHaveNoGuidAddressLookup()
    {
        typeof(CoffeeShopsController).GetMethod(nameof(CoffeeShopsController.GetCoffeeShop))!
            .GetCustomAttributes<HttpGetAttribute>().Select(x => x.Template).Should().BeEquivalentTo("{slug}", "by-slug/{slug}");
        typeof(RoastersController).GetMethod(nameof(RoastersController.GetById))!
            .GetCustomAttributes<HttpGetAttribute>().Select(x => x.Template).Should().BeEquivalentTo("{slug}", "by-slug/{slug}");
        foreach (var type in new[] { typeof(CoffeeShopsController), typeof(RoastersController), typeof(PublicCityAddressesController), typeof(PublicCoffeeZoneAddressesController) })
            type.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Select(x => x.Template)
                .Should().NotContain(x => x != null && (x.Contains(":guid") || x.Contains("public-address")));
    }

    [Fact]
    public void PublicAddressReads_AreNeverResponseCached()
    {
        foreach (var controller in new[]
                 {
                     typeof(CoffeeShopsController), typeof(RoastersController),
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
        var unknown = () => AdminKind("unknown");
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
