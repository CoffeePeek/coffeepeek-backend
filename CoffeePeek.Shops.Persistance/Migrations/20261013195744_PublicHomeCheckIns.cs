using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class PublicHomeCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_KindShape",
                table: "CanonicalCheckIns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_KindShape",
                table: "CanonicalCheckIns",
                sql: "(\"Kind\" = 0 AND \"ShopId\" IS NOT NULL AND \"PreparedOn\" IS NULL AND \"BrewMethodCode\" IS NULL AND \"CoffeeProductId\" IS NULL AND \"BeanNameSnapshot\" IS NULL AND \"DoseGrams\" IS NULL AND \"BrewDurationSeconds\" IS NULL AND \"TasteScore\" IS NULL) OR (\"Kind\" = 1 AND \"ShopId\" IS NULL AND \"PreparedOn\" IS NOT NULL AND \"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_KindShape",
                table: "CanonicalCheckIns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_KindShape",
                table: "CanonicalCheckIns",
                sql: "(\"Kind\" = 0 AND \"ShopId\" IS NOT NULL AND \"PreparedOn\" IS NULL AND \"BrewMethodCode\" IS NULL AND \"CoffeeProductId\" IS NULL AND \"BeanNameSnapshot\" IS NULL AND \"DoseGrams\" IS NULL AND \"BrewDurationSeconds\" IS NULL AND \"TasteScore\" IS NULL) OR (\"Kind\" = 1 AND \"ShopId\" IS NULL AND \"PreparedOn\" IS NOT NULL AND \"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL AND \"Visibility\" = 0 AND \"ModerationState\" = 0 AND \"ApprovedRevision\" IS NULL AND \"ModerationSubmissionId\" IS NULL AND \"FirstPublishedAtUtc\" IS NULL AND \"HelpfulCount\" = 0)");
        }
    }
}
