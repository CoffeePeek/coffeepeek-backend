using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedPhotoSchedulingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_Status_ScheduledDeletionAt",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "PermalinkExpiresAt",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ScheduledDeletionAt",
                table: "Photos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PermalinkExpiresAt",
                table: "Photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDeletionAt",
                table: "Photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_Status_ScheduledDeletionAt",
                table: "Photos",
                columns: new[] { "Status", "ScheduledDeletionAt" });
        }
    }
}
