using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class HomeBrewMethodCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeDetails",
                table: "CanonicalCheckIns");

            migrationBuilder.AlterColumn<string>(
                name: "BrewMethodCode",
                table: "CanonicalCheckIns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeDetails",
                table: "CanonicalCheckIns",
                sql: "(\"BrewMethodCode\" IS NULL OR length(\"BrewMethodCode\") BETWEEN 1 AND 100) AND (\"CoffeeProductId\" IS NULL OR \"BeanNameSnapshot\" IS NOT NULL AND length(btrim(\"BeanNameSnapshot\")) > 0) AND (\"DoseGrams\" IS NULL OR \"DoseGrams\" > 0 AND \"DoseGrams\" <= 10000) AND (\"BrewDurationSeconds\" IS NULL OR \"BrewDurationSeconds\" BETWEEN 1 AND 604800) AND (\"TasteScore\" IS NULL OR \"TasteScore\" BETWEEN 1 AND 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "CanonicalCheckIns" WHERE "BrewMethodCode" IS NOT NULL
                        AND "BrewMethodCode" NOT IN ('espresso', 'pour_over', 'batch_drip', 'moka_pot', 'cezve', 'aeropress', 'french_press', 'other')) THEN
                        RAISE EXCEPTION 'Cannot roll back: home check-ins use catalog brew methods.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeDetails",
                table: "CanonicalCheckIns");

            migrationBuilder.AlterColumn<string>(
                name: "BrewMethodCode",
                table: "CanonicalCheckIns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeDetails",
                table: "CanonicalCheckIns",
                sql: "(\"BrewMethodCode\" IS NULL OR \"BrewMethodCode\" IN ('espresso', 'pour_over', 'batch_drip', 'moka_pot', 'cezve', 'aeropress', 'french_press', 'other')) AND (\"CoffeeProductId\" IS NULL OR \"BeanNameSnapshot\" IS NOT NULL AND length(btrim(\"BeanNameSnapshot\")) > 0) AND (\"DoseGrams\" IS NULL OR \"DoseGrams\" > 0 AND \"DoseGrams\" <= 10000) AND (\"BrewDurationSeconds\" IS NULL OR \"BrewDurationSeconds\" BETWEEN 1 AND 604800) AND (\"TasteScore\" IS NULL OR \"TasteScore\" BETWEEN 1 AND 5)");
        }
    }
}
