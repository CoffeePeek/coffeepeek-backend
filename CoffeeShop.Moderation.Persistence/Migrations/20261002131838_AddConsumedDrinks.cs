using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumedDrinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomDrinkName",
                table: "ModerationReviews",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrinkNameEn",
                table: "ModerationReviews",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrinkNameRu",
                table: "ModerationReviews",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrinkSlug",
                table: "ModerationReviews",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomDrinkName",
                table: "ModerationReviews");

            migrationBuilder.DropColumn(
                name: "DrinkNameEn",
                table: "ModerationReviews");

            migrationBuilder.DropColumn(
                name: "DrinkNameRu",
                table: "ModerationReviews");

            migrationBuilder.DropColumn(
                name: "DrinkSlug",
                table: "ModerationReviews");
        }
    }
}
