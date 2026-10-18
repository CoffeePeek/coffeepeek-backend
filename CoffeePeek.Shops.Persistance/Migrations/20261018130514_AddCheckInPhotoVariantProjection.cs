using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInPhotoVariantProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInPhotoVariantManifests",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    CheckInId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInPhotoVariantManifests", x => new { x.PhotoId, x.SourceVersion, x.RecipeVersion });
                    table.ForeignKey(
                        name: "FK_CheckInPhotoVariantManifests_CheckInPhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "CheckInPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckInPhotoVariantManifests");
        }
    }
}
