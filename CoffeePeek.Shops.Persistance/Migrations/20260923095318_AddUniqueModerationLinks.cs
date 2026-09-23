using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueModerationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_ModerationReviewId",
                table: "Reviews");

            // Same deploy-safe pattern as AddUniqueShopModerationId: migrations run on startup, so legacy duplicates
            // degrade to a plain index with a warning instead of failing the deploy.
            migrationBuilder.Sql(UniqueOrPlain("IX_Roasters_ModerationId", "Roasters", "ModerationId"));
            migrationBuilder.Sql(UniqueOrPlain("IX_Reviews_ModerationReviewId", "Reviews", "ModerationReviewId"));
        }

        private static string UniqueOrPlain(string index, string table, string column) => $"""
            DO $$
            BEGIN
                CREATE UNIQUE INDEX "{index}" ON "{table}" ("{column}") WHERE "{column}" IS NOT NULL;
            EXCEPTION WHEN unique_violation THEN
                RAISE WARNING 'Duplicate {table}.{column} values exist; created non-unique {index}';
                CREATE INDEX "{index}" ON "{table}" ("{column}") WHERE "{column}" IS NOT NULL;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roasters_ModerationId",
                table: "Roasters");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_ModerationReviewId",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ModerationReviewId",
                table: "Reviews",
                column: "ModerationReviewId");
        }
    }
}
