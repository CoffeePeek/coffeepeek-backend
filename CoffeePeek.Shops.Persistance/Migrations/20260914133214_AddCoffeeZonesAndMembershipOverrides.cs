using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeZonesAndMembershipOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoffeeZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CenterLatitude = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    CenterLongitude = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    RadiusMeters = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeZones", x => x.Id);
                    table.CheckConstraint("CK_CoffeeZones_Latitude", "\"CenterLatitude\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_CoffeeZones_Longitude", "\"CenterLongitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_CoffeeZones_RadiusMeters", "\"RadiusMeters\" BETWEEN 100 AND 2000");
                    table.CheckConstraint("CK_CoffeeZones_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_CoffeeZones_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeZoneMembershipOverrides",
                columns: table => new
                {
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeZoneMembershipOverrides", x => new { x.ZoneId, x.ShopId });
                    table.CheckConstraint("CK_CoffeeZoneMembershipOverrides_Kind", "\"Kind\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_CoffeeZoneMembershipOverrides_CoffeeZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "CoffeeZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoffeeZoneMembershipOverrides_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_CoffeeZoneMembershipOverrides_PrimaryShop",
                table: "CoffeeZoneMembershipOverrides",
                column: "ShopId",
                unique: true,
                filter: "\"Kind\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeZones_CityId_Status",
                table: "CoffeeZones",
                columns: new[] { "CityId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoffeeZoneMembershipOverrides");

            migrationBuilder.DropTable(
                name: "CoffeeZones");
        }
    }
}
