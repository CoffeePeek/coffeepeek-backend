using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.ShopsService.Tests.Architecture;

/// <summary>
/// Guards the existing inward dependency direction while the service is modularized.
/// Module ownership rules will be added once the table and operation owners are fixed.
/// </summary>
public class LayerDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceOuterLayersOrFrameworkAdapters()
    {
        var references = typeof(CoffeeShop).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("CoffeePeek.Shops.Application", StringComparison.Ordinal)
            || name.StartsWith("CoffeePeek.Shops.Persistance", StringComparison.Ordinal)
            || name.StartsWith("CoffeePeek.Shops.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("CoffeePeek.ShopsService", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || name.StartsWith("Wolverine", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotReferenceConcreteAdaptersOrHost()
    {
        var references = typeof(UpdateAdminCoffeeShopHandler).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("CoffeePeek.Shops.Persistance", StringComparison.Ordinal)
            || name.StartsWith("CoffeePeek.Shops.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("CoffeePeek.ShopsService", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
