using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalSavedCollectionDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "SavedCollections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_SavedCollections_OwnerId",
                table: "SavedCollections",
                column: "OwnerId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"RevokedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedCollections_OwnerId",
                table: "SavedCollections");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "SavedCollections");
        }
    }
}
