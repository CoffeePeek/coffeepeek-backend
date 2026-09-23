using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueShopModerationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrations run on startup: if legacy duplicate shops already exist, fall back to a plain index instead of
            // failing the deploy. Clean the duplicates up and re-run to get the unique constraint.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE UNIQUE INDEX "IX_Shops_ModerationId" ON "Shops" ("ModerationId") WHERE "ModerationId" IS NOT NULL;
                EXCEPTION WHEN unique_violation THEN
                    RAISE WARNING 'Duplicate Shops.ModerationId values exist; created non-unique IX_Shops_ModerationId';
                    CREATE INDEX "IX_Shops_ModerationId" ON "Shops" ("ModerationId") WHERE "ModerationId" IS NOT NULL;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shops_ModerationId",
                table: "Shops");
        }
    }
}
