using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipEvidencePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "EvidencePhotoIds",
                table: "BusinessOwnershipClaims",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BusinessOwnershipClaims")
                     OR EXISTS (SELECT 1 FROM "BusinessOwnershipChanges") THEN
                    RAISE EXCEPTION 'Ownership history exists; retain this evidence schema and roll forward instead';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "EvidencePhotoIds",
                table: "BusinessOwnershipClaims");
        }
    }
}
