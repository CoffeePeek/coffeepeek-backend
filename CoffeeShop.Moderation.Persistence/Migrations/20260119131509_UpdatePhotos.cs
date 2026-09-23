using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UploadedAt",
                table: "ShopPhotos",
                newName: "CreatedAtUtc");

            migrationBuilder.AddColumn<Guid>(
                name: "ModerationReviewId",
                table: "ShopPhotos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "ShopPhotos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopPhotos_ModerationReviewId",
                table: "ShopPhotos",
                column: "ModerationReviewId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShopPhotos_ModerationReviews_ModerationReviewId",
                table: "ShopPhotos",
                column: "ModerationReviewId",
                principalTable: "ModerationReviews",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShopPhotos_ModerationReviews_ModerationReviewId",
                table: "ShopPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ShopPhotos_ModerationReviewId",
                table: "ShopPhotos");

            migrationBuilder.DropColumn(
                name: "ModerationReviewId",
                table: "ShopPhotos");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "ShopPhotos");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "ShopPhotos",
                newName: "UploadedAt");
        }
    }
}
