using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Persistance.Migrations;

[DbContext(typeof(ShopsDbContext))]
[Migration("20261018120000_AddNormalizedShopSearchIndexes")]
public sealed class AddNormalizedShopSearchIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Expression indexes keep normalized substring searches on the existing pg_trgm path.
        // These expressions have no corresponding EF property, so their indexes are managed here.
        migrationBuilder.Sql("""
            CREATE INDEX "IX_Shops_NormalizedName_GIN" ON "Shops"
                USING gin (replace(lower("Name"), 'ё', 'е') gin_trgm_ops);
            CREATE INDEX "IX_Shops_NormalizedAddress_GIN" ON "Shops"
                USING gin (replace(lower("Address"), 'ё', 'е') gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Shops_NormalizedName_GIN", table: "Shops");
        migrationBuilder.DropIndex(name: "IX_Shops_NormalizedAddress_GIN", table: "Shops");
    }
}
