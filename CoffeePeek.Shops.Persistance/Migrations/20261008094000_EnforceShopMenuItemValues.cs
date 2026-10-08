using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class EnforceShopMenuItemValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing rows may predate the positive present-offer rule. Fail with an actionable
        // count rather than silently deleting menu data or leaving a NOT VALID constraint.
        migrationBuilder.Sql(
            """
            DO $preflight$
            DECLARE invalid_count bigint;
            BEGIN
                SELECT count(*) INTO invalid_count
                FROM "ShopMenuItems"
                WHERE NOT (
                    "Availability" IN (0, 1, 2)
                    AND (("Availability" = 1 AND "Price" IS NOT NULL AND "Price" > 0
                          AND "VolumeMl" IS NOT NULL AND "VolumeMl" > 0)
                         OR ("Availability" <> 1 AND "Price" IS NULL AND "VolumeMl" IS NULL))
                );
                IF invalid_count > 0 THEN
                    RAISE EXCEPTION
                        'ShopMenuItems has % invalid value rows; remediate before migration.',
                        invalid_count;
                END IF;
            END
            $preflight$;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ShopMenuItems_Values",
            table: "ShopMenuItems",
            sql: "\"Availability\" IN (0, 1, 2) AND ((\"Availability\" = 1 AND \"Price\" IS NOT NULL AND \"Price\" > 0 AND \"VolumeMl\" IS NOT NULL AND \"VolumeMl\" > 0) OR (\"Availability\" <> 1 AND \"Price\" IS NULL AND \"VolumeMl\" IS NULL))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ShopMenuItems_Values",
            table: "ShopMenuItems");
    }
}
