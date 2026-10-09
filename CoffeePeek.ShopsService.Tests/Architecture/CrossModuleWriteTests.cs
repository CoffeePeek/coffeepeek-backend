using System.Reflection;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.ShopsService.Tests.Architecture;

public class CrossModuleWriteTests
{
    [Fact]
    public void ApplicationAndConsumers_OnlyWriteTheirOwnDomainOrUsePorts()
    {
        var application = typeof(UpdateAdminCoffeeShopHandler).Assembly;
        var infrastructure = typeof(ShopChangeRequestApprovedHandler).Assembly;
        var persistence = typeof(ShopsDbContext).Assembly;

        var actual = IlDependencyScanner.GetCalls(application)
            .Concat(IlDependencyScanner.GetCalls(infrastructure,
                type => type.Namespace?.StartsWith("CoffeePeek.Shops.Infrastructure.Consumers", StringComparison.Ordinal) == true))
            .Concat(IlDependencyScanner.GetCalls(persistence,
                type => type.Namespace is { } ns &&
                        (ns.StartsWith("CoffeePeek.Shops.Persistance.Repositories", StringComparison.Ordinal) ||
                         ns.StartsWith("CoffeePeek.Shops.Persistance.Menu.", StringComparison.Ordinal) ||
                         ns.StartsWith("CoffeePeek.Shops.Persistance.CoffeeCatalog", StringComparison.Ordinal) ||
                         ns.StartsWith("CoffeePeek.Shops.Persistance.Discovery", StringComparison.Ordinal) ||
                         ns.StartsWith("CoffeePeek.Shops.Persistance.PublicAddresses", StringComparison.Ordinal))))
            .Where(call => IsCrossModuleWrite(call.Caller, call.Callee))
            .Select(call => $"{CallerName(call.Caller)} -> " +
                            CalleeName(call.Callee))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void MenuPersistenceFixture_DirectShopMutationIsDetected()
    {
        var calls = IlDependencyScanner.GetCalls(
                typeof(CoffeePeek.Shops.Persistance.Menu.MenuCrossModuleWriteFixture).Assembly,
                type => type == typeof(CoffeePeek.Shops.Persistance.Menu.MenuCrossModuleWriteFixture))
            .Where(call => IsCrossModuleWrite(call.Caller, call.Callee))
            .Select(call => CalleeName(call.Callee))
            .ToArray();

        Assert.Contains("CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop.SetPriceRange", calls);
    }

    private static bool IsCrossModuleWrite(MethodBase caller, MethodBase callee)
    {
        var callerType = caller.DeclaringType;
        var calleeType = callee.DeclaringType;
        if (callerType is null || calleeType is null || !IsWriteOperation(callee.Name))
            return false;

        var owner = calleeType.IsGenericType && calleeType.GetGenericTypeDefinition() == typeof(DbSet<>)
            ? DomainOwner(calleeType.GetGenericArguments()[0])
            : DomainOwner(calleeType);
        if (calleeType.FullName == "CoffeePeek.Shared.Domain.PublicAddresses.IPublicSlugOwner" &&
            callee.Name == "SetPublicSlug")
            owner = "EntityOwner";
        return owner is not null && owner != CallerOwner(callerType);
    }

    private static string CalleeName(MethodBase callee)
    {
        var type = callee.DeclaringType!;
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbSet<>)
            ? $"DbSet<{type.GetGenericArguments()[0].FullName}>.{callee.Name}"
            : $"{type.FullName}.{callee.Name}";
    }

    private static string CallerName(MethodBase caller)
    {
        var type = caller.DeclaringType!;
        // C# assigns different display-class and lambda ordinals in Debug and Release.
        // Keep the enclosing method stable while retaining the called domain operation.
        if (type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)
            && type.DeclaringType is { } enclosing
            && caller.Name.StartsWith('<'))
        {
            var end = caller.Name.IndexOf('>');
            if (end > 1)
                return $"{enclosing.FullName}.{caller.Name[1..end]}::<lambda>";
        }
        return $"{type.FullName}.{caller.Name}";
    }

    private static bool IsWriteOperation(string name) =>
        name is ".ctor" or "Create" ||
        new[] { "Add", "Apply", "Archive", "Assign", "Change", "Clear", "Complete", "Mark", "Merge",
                "Publish", "Record", "Remove", "Replace", "Resolve", "Set", "Start", "Submit", "TryEnrich", "Update" }
            .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static string? DomainOwner(Type type)
    {
        var name = type.FullName ?? string.Empty;
        if (!name.StartsWith("CoffeePeek.Shops.Domain.", StringComparison.Ordinal))
            return null;
        if (name.Contains(".MenuAggregate.", StringComparison.Ordinal))
            return "Menus";
        if (name.Contains(".CoffeeProductAggregate.", StringComparison.Ordinal) ||
            name.Contains(".CoffeeShopAggregate.Roaster", StringComparison.Ordinal) ||
            name.Contains(".CoffeeShopAggregate.IRoasterRepository", StringComparison.Ordinal))
            return "CoffeeCatalog";
        if (name.Contains(".CheckInAggregate.", StringComparison.Ordinal) ||
            name.Contains(".ReviewAggregate.", StringComparison.Ordinal))
            return "Community";
        if (name.Contains(".DiscoveryAggregate.", StringComparison.Ordinal))
            return "Discovery";
        if (name == "CoffeePeek.Shops.Domain.Entities.ShopPhoto")
            return "Shops";
        if (name.Contains(".CoffeeShopAggregate.", StringComparison.Ordinal))
            return "Shops";
        return null;
    }

    private static string CallerOwner(Type type)
    {
        var name = type.FullName ?? string.Empty;
        if (name.Contains(".Features.Admin.Menu.", StringComparison.Ordinal) ||
            name.Contains(".Features.Menu.", StringComparison.Ordinal) ||
            name.Contains(".Features.Catalogs.Drinks.", StringComparison.Ordinal) ||
            name.Contains(".Features.Catalogs.GetMenuDrinks.", StringComparison.Ordinal) ||
            name.Contains(".Services.ApplyShopMenuService", StringComparison.Ordinal) ||
            name.Contains(".Consumers.ParseMenuRequestedEventHandler", StringComparison.Ordinal))
            return "Menus";
        if (name.Contains(".Persistance.Menu.", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.ShopMenuRepository", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.QueryShopMenuRepository", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.QueryCoffeeDrinkRepository", StringComparison.Ordinal))
            return "Menus";
        if (name.Contains(".Features.CheckIn.", StringComparison.Ordinal) ||
            name.Contains(".Features.Review.", StringComparison.Ordinal) ||
            name.Contains(".Features.Feed.", StringComparison.Ordinal) ||
            name.Contains(".Consumers.CheckIn", StringComparison.Ordinal) ||
            name.Contains(".Consumers.UserNameChangedEventHandler", StringComparison.Ordinal))
            return "Community";
        if (name.Contains(".Persistance.Repositories.QueryCheckInRepository", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.ReviewReportRepository", StringComparison.Ordinal))
            return "Community";
        if (name.Contains(".Features.CoffeeCatalog.", StringComparison.Ordinal) ||
            name.Contains(".Features.Admin.Catalogs.Roasters.", StringComparison.Ordinal) ||
            name.Contains(".Services.CreateRoasterFromModerationService", StringComparison.Ordinal) ||
            name.Contains(".Consumers.ModerationRoasterApproveHandler", StringComparison.Ordinal) ||
            name.Contains(".Persistance.CoffeeCatalog.", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.QueryRoasterRepository", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.RoasterRepository", StringComparison.Ordinal))
            return "CoffeeCatalog";
        if (name.Contains(".Features.Discovery.", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Discovery.", StringComparison.Ordinal))
            return "Discovery";
        if (name.Contains(".Features.AppDownloads.", StringComparison.Ordinal) ||
            name.Contains(".Persistance.Repositories.AppDistributionRepository", StringComparison.Ordinal))
            return "AppDistribution";
        if (name.Contains(".Persistance.PublicAddresses.", StringComparison.Ordinal))
            return "PublicAddresses";
        return "Shops";
    }
}
