using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPublicAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit");

            migrationBuilder.CreateTable(
                name: "BrewMethodPublicAddresses",
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
                    table.PrimaryKey("PK_BrewMethodPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_BrewMethodPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_BrewMethodPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_BrewMethodPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_BrewMethodPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_BrewMethodPublicAddresses_BrewMethods_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "BrewMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeBeanPublicAddresses",
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
                    table.PrimaryKey("PK_CoffeeBeanPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_CoffeeBeanPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_CoffeeBeanPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_CoffeeBeanPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_CoffeeBeanPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_CoffeeBeanPublicAddresses_CoffeeBeans_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "CoffeeBeans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentPublicAddresses",
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
                    table.PrimaryKey("PK_EquipmentPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_EquipmentPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_EquipmentPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_EquipmentPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_EquipmentPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_EquipmentPublicAddresses_Equipments_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShopTagPublicAddresses",
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
                    table.PrimaryKey("PK_ShopTagPublicAddresses", x => x.Id);
                    table.CheckConstraint("CK_ShopTagPublicAddresses_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
                    table.CheckConstraint("CK_ShopTagPublicAddresses_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_ShopTagPublicAddresses_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ShopTagPublicAddresses_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_ShopTagPublicAddresses_ShopTags_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ShopTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit",
                sql: "\"Kind\" IN (1, 2, 3, 4, 5, 6, 7, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_BrewMethodPublicAddresses_OwnerId",
                table: "BrewMethodPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BrewMethodPublicAddresses_OwnerId_State",
                table: "BrewMethodPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_BrewMethodPublicAddresses_Slug",
                table: "BrewMethodPublicAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeBeanPublicAddresses_OwnerId",
                table: "CoffeeBeanPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeBeanPublicAddresses_OwnerId_State",
                table: "CoffeeBeanPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeBeanPublicAddresses_Slug",
                table: "CoffeeBeanPublicAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPublicAddresses_OwnerId",
                table: "EquipmentPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPublicAddresses_OwnerId_State",
                table: "EquipmentPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPublicAddresses_Slug",
                table: "EquipmentPublicAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopTagPublicAddresses_OwnerId",
                table: "ShopTagPublicAddresses",
                column: "OwnerId",
                unique: true,
                filter: "\"State\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ShopTagPublicAddresses_OwnerId_State",
                table: "ShopTagPublicAddresses",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopTagPublicAddresses_Slug",
                table: "ShopTagPublicAddresses",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrewMethodPublicAddresses");

            migrationBuilder.DropTable(
                name: "CoffeeBeanPublicAddresses");

            migrationBuilder.DropTable(
                name: "EquipmentPublicAddresses");

            migrationBuilder.DropTable(
                name: "ShopTagPublicAddresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicAddressAudit_Kind",
                table: "PublicAddressAudit",
                sql: "\"Kind\" IN (1, 2, 3, 4)");
        }
    }
}
