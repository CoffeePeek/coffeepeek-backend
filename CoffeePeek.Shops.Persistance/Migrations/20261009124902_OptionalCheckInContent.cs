using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class OptionalCheckInContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_CompleteContent",
                table: "CanonicalCheckIns");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Service",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Place",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Coffee",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rating_AverageRating",
                table: "CanonicalCheckIns",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_CompleteContent",
                table: "CanonicalCheckIns",
                sql: "\"Note\" IS NOT NULL AND length(btrim(\"Note\")) <= 1000 AND ((\"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL) OR (\"Rating_Place\" IS NOT NULL AND \"Rating_Service\" IS NOT NULL AND \"Rating_Coffee\" IS NOT NULL AND \"Rating_AverageRating\" IS NOT NULL AND \"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_CompleteContent",
                table: "CanonicalCheckIns");

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Service",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Place",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating_Coffee",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Rating_AverageRating",
                table: "CanonicalCheckIns",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_CompleteContent",
                table: "CanonicalCheckIns",
                sql: "(\"Note\" IS NOT NULL AND length(btrim(\"Note\")) BETWEEN 1 AND 1000 AND \"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5)");
        }
    }
}
