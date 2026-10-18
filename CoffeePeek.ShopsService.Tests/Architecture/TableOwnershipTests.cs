using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.ShopsService.Tests.Architecture;

/// <summary>
/// Every EF-mapped table must have exactly one declared business owner. This is an
/// inventory guard; write-path restrictions are a separate architectural check.
/// </summary>
public class TableOwnershipTests
{
    private static readonly IReadOnlyDictionary<string, string[]> TablesByOwner =
        new Dictionary<string, string[]>
        {
            ["Shops"] =
            [
                "Shops", "Cities", "CoffeeZones", "CoffeeZoneMembershipOverrides",
                "ShopSchedule", "ShopScheduleInterval", "ShopPhotos", "AppliedShopChangeRequests",
                "BrewMethods", "CoffeeBeans", "EquipmentCategories", "Equipments",
                "ShopTags", "CoffeeShopTags", "CoffeeShopBrewMethods", "CoffeeShopCoffeeBeans",
                "CoffeeShopEquipments", "CoffeeShopRoasters"
            ],
            ["Menus"] = ["CoffeeDrinkDefinitions", "ShopMenus", "ShopMenuItems", "ShopMenuPhotos", "MenuOcrJobs"],
            ["Community"] = ["CanonicalCheckIns", "CheckInPhotos", "CheckInPhotoVariantManifests", "CheckInHelpfulVotes", "ReviewReports"],
            ["CoffeeCatalog"] =
            [
                "Roasters", "RoasterPhotos", "RoasterTags", "RoasterTagAssignments",
                "CoffeeProducts", "CoffeeProductCountries", "CoffeeProductSources",
                "CoffeeProductPhotos", "CoffeeVariants", "CoffeeImportRuns", "CoffeeFilterValues", "Countries"
            ],
            ["Discovery"] = ["DiscoveryFavorites"],
            ["AppDistribution"] =
                ["AndroidReleases", "AppDistributionSettings", "AppDistributionAuditLogs", "AppDownloadRedirectEvents"]
        };

    [Fact]
    public void EveryMappedTable_HasOneDeclaredOwner()
    {
        var declared = TablesByOwner.SelectMany(group => group.Value.Select(table => (Table: table, Owner: group.Key)))
            .ToArray();
        var duplicate = declared.GroupBy(entry => entry.Table, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicate);

        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;
        using var db = new ShopsDbContext(options);
        var mapped = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .Select(table => table!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(table => table, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(mapped, declared.Select(entry => entry.Table)
            .OrderBy(table => table, StringComparer.Ordinal));
    }
}
