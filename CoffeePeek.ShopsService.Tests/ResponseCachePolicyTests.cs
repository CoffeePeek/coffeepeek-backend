using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Tests;

public class ResponseCachePolicyTests
{
    [Fact]
    public void MutableCatalogs_DoNotAllowHttpResponseCaching()
    {
        var attribute = GetResponseCacheAttribute(typeof(CatalogsController));

        attribute.NoStore.Should().BeTrue();
        attribute.Location.Should().Be(ResponseCacheLocation.None);
    }

    [Theory]
    [InlineData(typeof(PublicStatsController), nameof(PublicStatsController.GetStats))]
    [InlineData(typeof(AppDownloadsController), nameof(AppDownloadsController.Get))]
    [InlineData(typeof(AppDownloadsController), nameof(AppDownloadsController.GetVersion))]
    [InlineData(typeof(MenuController), nameof(MenuController.GetDrinks))]
    public void MutablePublicEndpoints_DoNotAllowHttpResponseCaching(Type controllerType, string actionName)
    {
        var method = controllerType.GetMethod(actionName);
        method.Should().NotBeNull();

        var attributes = method!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .ToArray();
        var attribute = attributes.Length == 1
            ? attributes[0]
            : GetResponseCacheAttribute(controllerType);

        attribute.NoStore.Should().BeTrue();
        attribute.Location.Should().Be(ResponseCacheLocation.None);
    }

    private static ResponseCacheAttribute GetResponseCacheAttribute(Type controllerType) =>
        controllerType
            .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .Single();
}
