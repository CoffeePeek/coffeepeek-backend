using System.Reflection;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace CoffeePeek.ShopsService.Tests;

public class DrinkCatalogAuthorizationTests
{
    [Fact]
    public void EveryAdminActionRequiresAdminPolicy()
    {
        foreach (var name in new[] { "GetAdminDrinks", "Create", "Update" })
        {
            var method = typeof(DrinkCatalogController).GetMethod(name)!;
            method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Admin);
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        }
        typeof(DrinkCatalogController).GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        typeof(DrinkCatalogController).GetMethod("GetDrinks")!
            .GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }
}
