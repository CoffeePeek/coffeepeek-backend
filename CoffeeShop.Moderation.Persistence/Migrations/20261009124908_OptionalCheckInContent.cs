using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptionalCheckInContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ModerationCheckIns_CanonicalSnapshot",
                table: "ModerationCheckIns");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Service",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Place",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Coffee",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rating_AverageRating",
                table: "ModerationCheckIns",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModerationCheckIns_CanonicalSnapshot",
                table: "ModerationCheckIns",
                sql: "\"CheckInId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ContentRevision\" > 0 AND \"Comment\" IS NOT NULL AND length(btrim(\"Comment\")) <= 1000 AND ((\"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL) OR (\"Rating_Place\" IS NOT NULL AND \"Rating_Service\" IS NOT NULL AND \"Rating_Coffee\" IS NOT NULL AND \"Rating_AverageRating\" IS NOT NULL AND \"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ModerationCheckIns_CanonicalSnapshot",
                table: "ModerationCheckIns");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Service",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Place",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Coffee",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Rating_AverageRating",
                table: "ModerationCheckIns",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModerationCheckIns_CanonicalSnapshot",
                table: "ModerationCheckIns",
                sql: "\"CheckInId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ContentRevision\" > 0 AND length(btrim(\"Comment\")) > 0 AND \"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5");
        }
    }
}
