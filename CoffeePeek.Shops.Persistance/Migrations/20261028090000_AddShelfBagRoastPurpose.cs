using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfBagRoastPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoastPurpose",
                table: "ShelfBags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShelfBags_RoastPurpose",
                table: "ShelfBags",
                sql: "\"RoastPurpose\" IS NULL OR \"RoastPurpose\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShelfBags_RoastPurpose",
                table: "ShelfBags");

            migrationBuilder.DropColumn(
                name: "RoastPurpose",
                table: "ShelfBags");
        }
    }
}
