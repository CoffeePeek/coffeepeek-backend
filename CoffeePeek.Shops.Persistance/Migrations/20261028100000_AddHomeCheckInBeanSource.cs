using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeCheckInBeanSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoasterNameSnapshot",
                table: "CanonicalCheckIns",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShelfBagId",
                table: "CanonicalCheckIns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCheckIns_UserId_ShelfBagId",
                table: "CanonicalCheckIns",
                columns: new[] { "UserId", "ShelfBagId" },
                filter: "\"ShelfBagId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeBeanSource",
                table: "CanonicalCheckIns",
                sql: "\"Kind\" = 1 OR (\"ShelfBagId\" IS NULL AND \"RoasterNameSnapshot\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeBeanSource",
                table: "CanonicalCheckIns");

            migrationBuilder.DropIndex(
                name: "IX_CanonicalCheckIns_UserId_ShelfBagId",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "RoasterNameSnapshot",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "ShelfBagId",
                table: "CanonicalCheckIns");
        }
    }
}
