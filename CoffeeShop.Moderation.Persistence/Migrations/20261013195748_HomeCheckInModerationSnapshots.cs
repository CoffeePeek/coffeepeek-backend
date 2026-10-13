using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HomeCheckInModerationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ShopId",
                table: "ModerationCheckIns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Home_BeanName",
                table: "ModerationCheckIns",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Home_BrewDurationSeconds",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Home_BrewMethodCode",
                table: "ModerationCheckIns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Home_CoffeeProductId",
                table: "ModerationCheckIns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Home_DoseGrams",
                table: "ModerationCheckIns",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Home_PreparedOn",
                table: "ModerationCheckIns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Home_TasteScore",
                table: "ModerationCheckIns",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ModerationCheckIns" WHERE "ShopId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back while home check-in moderation snapshots exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "Home_BeanName",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_BrewDurationSeconds",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_BrewMethodCode",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_CoffeeProductId",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_DoseGrams",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_PreparedOn",
                table: "ModerationCheckIns");

            migrationBuilder.DropColumn(
                name: "Home_TasteScore",
                table: "ModerationCheckIns");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShopId",
                table: "ModerationCheckIns",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
