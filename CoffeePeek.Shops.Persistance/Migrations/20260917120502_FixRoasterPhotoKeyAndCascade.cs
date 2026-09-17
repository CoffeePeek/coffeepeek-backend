using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class FixRoasterPhotoKeyAndCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoasterPhotos_Roasters_RoasterId",
                table: "RoasterPhotos");

            // Drop orphaned photos left by pre-fix failed edits before the FK becomes required.
            migrationBuilder.Sql("DELETE FROM \"RoasterPhotos\" WHERE \"RoasterId\" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoasterId",
                table: "RoasterPhotos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RoasterPhotos_Roasters_RoasterId",
                table: "RoasterPhotos",
                column: "RoasterId",
                principalTable: "Roasters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoasterPhotos_Roasters_RoasterId",
                table: "RoasterPhotos");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoasterId",
                table: "RoasterPhotos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_RoasterPhotos_Roasters_RoasterId",
                table: "RoasterPhotos",
                column: "RoasterId",
                principalTable: "Roasters",
                principalColumn: "Id");
        }
    }
}
