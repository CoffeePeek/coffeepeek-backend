using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddDrinkFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_DiscoveryFavorites_Kind",
                table: "DiscoveryFavorites",
                sql: "\"Kind\" IN ('coffee_shop', 'roaster', 'drink')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DiscoveryFavorites_Kind",
                table: "DiscoveryFavorites");
        }
    }
}
