using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CoffeePeek.ShopsService.Tests;

public sealed class PublicAddressApiContractTests
{
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
    }

    [Fact]
    public void PublicAddressReads_AreNeverResponseCached()
    {
        foreach (var controller in new[]
                 {
                     typeof(PublicCoffeeShopAddressesController), typeof(PublicRoasterAddressesController)
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
        Route(typeof(AdminPublicAddressesController))
            .Should().Be("api/admin/{entity:regex(^(shops|roasters)$)}/public-addresses");
    }

    private static string? Route(Type controller) => controller
        .GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template;

    private static string? Template<T>(string method) => typeof(T).GetMethod(method)!
        .GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single().Template;
}
