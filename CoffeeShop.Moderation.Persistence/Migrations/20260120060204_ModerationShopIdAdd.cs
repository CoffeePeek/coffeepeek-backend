using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModerationShopIdAdd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ModerationShops");

            migrationBuilder.AddColumn<Guid>(
                name: "ShopId",
                table: "ModerationShops",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ModerationShopId",
                table: "ModerationReviews",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ModerationShopId",
                table: "ModerationReviews",
                column: "ModerationShopId");

            migrationBuilder.AddForeignKey(
                name: "FK_ModerationReviews_ModerationShops_ModerationShopId",
                table: "ModerationReviews",
                column: "ModerationShopId",
                principalTable: "ModerationShops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModerationReviews_ModerationShops_ModerationShopId",
                table: "ModerationReviews");

            migrationBuilder.DropIndex(
                name: "IX_ModerationReviews_ModerationShopId",
                table: "ModerationReviews");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "ModerationShops");

            migrationBuilder.DropColumn(
                name: "ModerationShopId",
                table: "ModerationReviews");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ModerationShops",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
