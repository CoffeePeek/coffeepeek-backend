using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCityAndZonePublicAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit");

            migrationBuilder.CreateTable(
                name: "CityPublicAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_CityPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_CityPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_CityPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_CityPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_CityPublicAddresses_Cities_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeZonePublicAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeZonePublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_CoffeeZonePublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_CoffeeZonePublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_CoffeeZonePublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_CoffeeZonePublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "CoffeeZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit",
                sql: "\"Kind\" IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_CityPublicAddresses_OwnerId",
                table: "CityPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CityPublicAddresses_OwnerId_State",
                table: "CityPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CityPublicAddresses_Slug",
                table: "CityPublicAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeZonePublicAddresses_OwnerId",
                table: "CoffeeZonePublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeZonePublicAddresses_OwnerId_State",
                table: "CoffeeZonePublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeZonePublicAddresses_Slug",
                table: "CoffeeZonePublicAddresses",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CityPublicAddresses");

            migrationBuilder.DropTable(
                name: "CoffeeZonePublicAddresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit",
                sql: "\"Kind\" IN (1, 2)");
        }
    }
}
