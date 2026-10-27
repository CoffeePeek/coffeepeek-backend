using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfBagPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShelfBagId",
                table: "Photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ShelfBagId",
                table: "Photos",
                column: "ShelfBagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_ShelfBagId",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ShelfBagId",
                table: "Photos");
        }
    }
}
