using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipEvidencePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnershipClaimId",
                table: "Photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_OwnershipClaimId",
                table: "Photos",
                column: "OwnershipClaimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Photos" WHERE "OwnershipClaimId" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Ownership evidence history exists; retain these reservations and roll forward instead';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Photos_OwnershipClaimId",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "OwnershipClaimId",
                table: "Photos");
        }
    }
}
