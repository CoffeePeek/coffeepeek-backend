using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationQueueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopIssueReports_Status",
                table: "ShopIssueReports");

            migrationBuilder.DropIndex(
                name: "IX_ModerationShops_ModerationStatus",
                table: "ModerationShops");

            migrationBuilder.DropIndex(
                name: "IX_ModerationRoasters_ModerationStatus",
                table: "ModerationRoasters");

            migrationBuilder.DropIndex(
                name: "IX_ModerationReviews_ModerationStatus",
                table: "ModerationReviews");

            migrationBuilder.CreateIndex(
                name: "IX_ShopIssueReports_Status_CreatedAtUtc",
                table: "ShopIssueReports",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_ModerationStatus_CreatedAtUtc",
                table: "ModerationShops",
                columns: new[] { "ModerationStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_ShopId",
                table: "ModerationShops",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasters_ModerationStatus_CreatedAtUtc",
                table: "ModerationRoasters",
                columns: new[] { "ModerationStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ModerationStatus_CreatedAtUtc",
                table: "ModerationReviews",
                columns: new[] { "ModerationStatus", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopIssueReports_Status_CreatedAtUtc",
                table: "ShopIssueReports");

            migrationBuilder.DropIndex(
                name: "IX_ModerationShops_ModerationStatus_CreatedAtUtc",
                table: "ModerationShops");

            migrationBuilder.DropIndex(
                name: "IX_ModerationShops_ShopId",
                table: "ModerationShops");

            migrationBuilder.DropIndex(
                name: "IX_ModerationRoasters_ModerationStatus_CreatedAtUtc",
                table: "ModerationRoasters");

            migrationBuilder.DropIndex(
                name: "IX_ModerationReviews_ModerationStatus_CreatedAtUtc",
                table: "ModerationReviews");

            migrationBuilder.CreateIndex(
                name: "IX_ShopIssueReports_Status",
                table: "ShopIssueReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_ModerationStatus",
                table: "ModerationShops",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasters_ModerationStatus",
                table: "ModerationRoasters",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ModerationStatus",
                table: "ModerationReviews",
                column: "ModerationStatus");
        }
    }
}
