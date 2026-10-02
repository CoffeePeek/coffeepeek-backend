using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeZoneColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "CoffeeZones",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#F59E0B");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CoffeeZones_Color",
                table: "CoffeeZones",
                sql: "\"Color\" ~ '^#[0-9A-F]{6}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CoffeeZones_Color",
                table: "CoffeeZones");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "CoffeeZones");
        }
    }
}
