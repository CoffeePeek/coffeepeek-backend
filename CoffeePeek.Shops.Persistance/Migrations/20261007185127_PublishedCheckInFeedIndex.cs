using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class PublishedCheckInFeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCheckIns_FirstPublishedAtUtc_Id",
                table: "CanonicalCheckIns",
                columns: new[] { "FirstPublishedAtUtc", "Id" },
                descending: new bool[0],
                filter: "\"DeletedAtUtc\" IS NULL AND \"Visibility\" = 1 AND \"ModerationState\" = 2 AND \"ApprovedRevision\" = \"ContentRevision\" AND \"FirstPublishedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CanonicalCheckIns_FirstPublishedAtUtc_Id",
                table: "CanonicalCheckIns");
        }
    }
}
