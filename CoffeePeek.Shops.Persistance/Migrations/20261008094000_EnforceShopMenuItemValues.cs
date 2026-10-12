using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class EnforceShopMenuItemValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Approved legacy repair keeps menu rows and discards only invalid offer values.
        migrationBuilder.Sql(
            """
            UPDATE "ShopMenuItems"
            SET "Availability" = 0, "Price" = NULL, "VolumeMl" = NULL
            WHERE NOT (
                "Availability" IN (0, 1, 2)
                AND (("Availability" = 1 AND "Price" IS NOT NULL AND "Price" > 0
                      AND "VolumeMl" IS NOT NULL AND "VolumeMl" > 0)
                     OR ("Availability" <> 1 AND "Price" IS NULL AND "VolumeMl" IS NULL))
            );
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
