using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddAppVersionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AndroidLatestVersionCode",
                table: "AppDistributionSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AndroidMinVersionCode",
                table: "AppDistributionSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IosLatestVersionCode",
                table: "AppDistributionSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IosMinVersionCode",
                table: "AppDistributionSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AndroidLatestVersionCode",
                table: "AppDistributionSettings");

            migrationBuilder.DropColumn(
                name: "AndroidMinVersionCode",
                table: "AppDistributionSettings");

            migrationBuilder.DropColumn(
                name: "IosLatestVersionCode",
                table: "AppDistributionSettings");

            migrationBuilder.DropColumn(
                name: "IosMinVersionCode",
                table: "AppDistributionSettings");
        }
    }
}
