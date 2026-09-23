using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace CoffeePeek.ShopsService.Tests;

public sealed class CoffeeZoneAuthorizationTests
{
    [Fact]
    public void AdminCoffeeZones_RequiresModeratorPolicy()
    {
        var attribute = typeof(AdminCoffeeZonesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(RoleConsts.Moderator, attribute.Policy);
    }

    [Fact]
    public void PublicMap_RemainsAnonymous()
    {
        var attributes = typeof(MapController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true);

        Assert.Single(attributes);
    }
}
