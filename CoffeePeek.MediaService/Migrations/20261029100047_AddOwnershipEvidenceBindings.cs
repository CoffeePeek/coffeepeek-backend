using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipEvidenceBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnershipEvidenceBindings",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnershipEvidenceBindings", x => new { x.PhotoId, x.ClaimId });
                    table.ForeignKey(
                        name: "FK_OwnershipEvidenceBindings_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "OwnershipEvidenceBindings") THEN
                    RAISE EXCEPTION 'Ownership evidence history exists; retain these bindings and roll forward instead';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "OwnershipEvidenceBindings");
        }
    }
}
