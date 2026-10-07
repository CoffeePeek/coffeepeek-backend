using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalCheckInPhotoClaimsOnMain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckInId",
                table: "Photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAtUtc",
                table: "Photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalStorageKey",
                table: "Photos",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CheckInId",
                table: "Photos",
                column: "CheckInId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_CheckInId",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "CheckInId",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "OriginalStorageKey",
                table: "Photos");
        }
    }
}
