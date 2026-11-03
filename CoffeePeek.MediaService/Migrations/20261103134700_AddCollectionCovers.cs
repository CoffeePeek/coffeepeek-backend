using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CollectionCoverValidatedAtUtc",
                table: "Photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollectionId",
                table: "Photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CollectionId",
                table: "Photos",
                column: "CollectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Photos" WHERE "CollectionId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Collection cover bindings exist; preserve them before schema rollback.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Photos_CollectionId",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "CollectionCoverValidatedAtUtc",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Photos");
        }
    }
}
