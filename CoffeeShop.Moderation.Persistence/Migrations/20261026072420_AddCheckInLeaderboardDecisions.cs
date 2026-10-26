using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInLeaderboardDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeaderboardGeoStatus",
                table: "ModerationCheckIns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "MissingLocation");

            migrationBuilder.AddColumn<bool>(
                name: "LeaderboardOverride",
                table: "ModerationCheckIns",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaderboardReason",
                table: "ModerationCheckIns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LocationAccuracyMeters",
                table: "ModerationCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationCapturedAtUtc",
                table: "ModerationCheckIns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShopDistanceMeters",
                table: "ModerationCheckIns",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaderboardGeoStatus",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardOverride",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardReason",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "LocationAccuracyMeters",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "LocationCapturedAtUtc",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "ShopDistanceMeters",
                table: "ModerationCheckIns");
        }
    }
}
