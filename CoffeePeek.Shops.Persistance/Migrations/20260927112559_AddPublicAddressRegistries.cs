using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicAddressRegistries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoffeeShopPublicAddresses",
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
                    table.PrimaryKey("PK_CoffeeShopPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_CoffeeShopPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_CoffeeShopPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_CoffeeShopPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_CoffeeShopPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_CoffeeShopPublicAddresses_Shops_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoasterPublicAddresses",
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
                    table.PrimaryKey("PK_RoasterPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_RoasterPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_RoasterPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_RoasterPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_RoasterPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_RoasterPublicAddresses_Roasters_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Roasters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeShopPublicAddresses_OwnerId",
                table: "CoffeeShopPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeShopPublicAddresses_OwnerId_State",
                table: "CoffeeShopPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeShopPublicAddresses_Slug",
                table: "CoffeeShopPublicAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoasterPublicAddresses_OwnerId",
                table: "RoasterPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RoasterPublicAddresses_OwnerId_State",
                table: "RoasterPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_RoasterPublicAddresses_Slug",
                table: "RoasterPublicAddresses",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoffeeShopPublicAddresses");

            migrationBuilder.DropTable(
                name: "RoasterPublicAddresses");
        }
    }
}
