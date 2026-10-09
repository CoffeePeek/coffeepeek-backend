using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Persistance.Configuration;

public static class CheckInPhotoSchema
{
    public static Task ExpandAsync(ShopsDbContext db, CancellationToken ct = default)
    {
        // Legacy transfer needs this additive expansion before the earlier archive-cleanup migration.
        var script = db.GetService<IMigrator>().GenerateScript("20261008094000_EnforceShopMenuItemValues",
            "20261009081229_SeparateCommunityCheckInPhotos", MigrationsSqlGenerationOptions.Idempotent);
        return db.Database.ExecuteSqlRawAsync(script, ct);
    }
}
