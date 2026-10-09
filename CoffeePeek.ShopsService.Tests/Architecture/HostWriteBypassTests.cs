using System.Reflection;
using CoffeePeek.ShopsService.Controllers;

namespace CoffeePeek.ShopsService.Tests.Architecture;

public class HostWriteBypassTests
{
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

        Assert.Empty(actual);
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
