using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDrinkVolumeFromMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShopMenuItems_Values",
                table: "ShopMenuItems");

            migrationBuilder.DropColumn(
                name: "VolumeMl",
                table: "ShopMenuItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShopMenuItems_Values",
                table: "ShopMenuItems",
                sql: "\"Availability\" IN (0, 1, 2) AND ((\"Availability\" = 1 AND \"Price\" IS NOT NULL AND \"Price\" > 0) OR (\"Availability\" <> 1 AND \"Price\" IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new System.NotSupportedException("Menu volumes were removed. Restore a backup before rolling back this migration.");
    }
}
