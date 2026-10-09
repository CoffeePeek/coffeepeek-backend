using System.Reflection;
using CoffeePeek.ShopsService.Controllers;

namespace CoffeePeek.ShopsService.Tests.Architecture;

public class HostWriteBypassTests
{
    // Temporary HTTP -> store/service writes. The controller should dispatch an application
    // command through the normal validation, transaction, and idempotency boundary.
    private static readonly IReadOnlyDictionary<string, string> KnownBypasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CoffeePeek.ShopsService.Controllers.AdminCoffeeClassificationController+<Create>d__3.MoveNext -> CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore.CreateValueAsync"] =
                "CoffeeCatalog owns CoffeeFilterValues; move the direct host write behind its command in P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminCoffeeClassificationController+<Deactivate>d__5.MoveNext -> CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore.DeactivateValueAsync"] =
                "CoffeeCatalog owns CoffeeFilterValues; move the direct host write behind its command in P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminCoffeeClassificationController+<Edit>d__4.MoveNext -> CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore.EditValueAsync"] =
                "CoffeeCatalog owns CoffeeFilterValues; move the direct host write behind its command in P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminCoffeeClassificationController+<EditClassification>d__7.MoveNext -> CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore.EditClassificationAsync"] =
                "CoffeeCatalog owns classification; move the direct host write behind its command in P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminCoffeeClassificationController+<EditVariantClassification>d__8.MoveNext -> CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore.EditVariantClassificationAsync"] =
                "CoffeeCatalog owns variant classification; move the direct host write behind its command in P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminPublicAddressesController+<Backfill>d__5.MoveNext -> CoffeePeek.Shops.Application.PublicAddresses.IPublicAddressService.BackfillAsync"] =
                "PublicAddresses coordinates entity owners; dispatch a command with one transaction in P20/P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminPublicAddressesController+<Initialize>d__3.MoveNext -> CoffeePeek.Shops.Application.PublicAddresses.IPublicAddressService.ChangeAsync"] =
                "PublicAddresses coordinates entity owners; dispatch a command with one transaction in P20/P25.",
            ["CoffeePeek.ShopsService.Controllers.AdminPublicAddressesController+<Rename>d__4.MoveNext -> CoffeePeek.Shops.Application.PublicAddresses.IPublicAddressService.ChangeAsync"] =
                "PublicAddresses coordinates entity owners; dispatch a command with one transaction in P20/P25.",
        };

    [Fact]
    public void Controllers_DoNotAddDirectApplicationWritePortCalls()
    {
        var host = typeof(DiscoveryFavoritesController).Assembly;
        var actual = IlDependencyScanner.GetCalls(host,
                type => type.Namespace?.StartsWith("CoffeePeek.ShopsService.Controllers", StringComparison.Ordinal) == true)
            .Where(call => IsDirectApplicationWrite(call.Callee))
            .Select(call => $"{call.Caller.DeclaringType?.FullName}.{call.Caller.Name} -> " +
                            $"{call.Callee.DeclaringType?.FullName}.{call.Callee.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expected = KnownBypasses.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.True(actual.SequenceEqual(expected, StringComparer.Ordinal),
            "Direct controller writes differ from the fixed, owner/task-annotated baseline.\n" +
            "New:\n" + string.Join("\n", actual.Except(expected, StringComparer.Ordinal)) +
            "\nStale baseline:\n" + string.Join("\n", expected.Except(actual, StringComparer.Ordinal)));
    }

    private static bool IsDirectApplicationWrite(MethodBase callee)
    {
        var type = callee.DeclaringType;
        if (type?.Namespace?.StartsWith("CoffeePeek.Shops.Application", StringComparison.Ordinal) != true)
            return false;

        return new[] { "Add", "Apply", "Backfill", "Change", "Create", "Deactivate", "Delete", "Edit",
                       "Fail", "Import", "Initialize", "Publish", "Record", "Remove",
                       "Set", "Submit", "Update" }
            .Any(prefix => callee.Name.StartsWith(prefix, StringComparison.Ordinal));
    }
}
