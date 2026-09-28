using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicAddressAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicAddressAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PreviousSlug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicAddressAudit", x => x.Id);
                    table.CheckConstraint("CK_PublicAddressAudit_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_PublicAddressAudit_Revision", "\"Revision\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicAddressAudit_Kind_OwnerId_Revision",
                table: "PublicAddressAudit",
                columns: new[] { "Kind", "OwnerId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicAddressAudit");
        }
    }
}
