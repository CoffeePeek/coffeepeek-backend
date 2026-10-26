using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInLeaderboardEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DeviceLatitude",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeviceLongitude",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaderboardCreditedAtUtc",
                table: "CanonicalCheckIns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaderboardGeoStatus",
                table: "CanonicalCheckIns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "MissingLocation");

            migrationBuilder.AddColumn<bool>(
                name: "LeaderboardOverride",
                table: "CanonicalCheckIns",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LeaderboardVersion",
                table: "CanonicalCheckIns",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<double>(
                name: "LocationAccuracyMeters",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationCapturedAtUtc",
                table: "CanonicalCheckIns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShopDistanceMeters",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VerifiedShopLatitude",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VerifiedShopLongitude",
                table: "CanonicalCheckIns",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceLatitude",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "DeviceLongitude",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardCreditedAtUtc",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardGeoStatus",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardOverride",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LeaderboardVersion",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LocationAccuracyMeters",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "LocationCapturedAtUtc",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "ShopDistanceMeters",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "VerifiedShopLatitude",
                table: "CanonicalCheckIns");

            migrationBuilder.DropColumn(
                name: "VerifiedShopLongitude",
                table: "CanonicalCheckIns");
        }
    }
}
