using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RatingService",
                table: "ModerationReviews",
                newName: "Rating_Service");

            migrationBuilder.RenameColumn(
                name: "RatingPlace",
                table: "ModerationReviews",
                newName: "Rating_Place");

            migrationBuilder.RenameColumn(
                name: "RatingCoffee",
                table: "ModerationReviews",
                newName: "Rating_Coffee");

            migrationBuilder.AlterColumn<int>(
                name: "PriceRange",
                table: "ModerationShops",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Rating_Service",
                table: "ModerationReviews",
                newName: "RatingService");

            migrationBuilder.RenameColumn(
                name: "Rating_Place",
                table: "ModerationReviews",
                newName: "RatingPlace");

            migrationBuilder.RenameColumn(
                name: "Rating_Coffee",
                table: "ModerationReviews",
                newName: "RatingCoffee");

            migrationBuilder.AlterColumn<int>(
                name: "PriceRange",
                table: "ModerationShops",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
