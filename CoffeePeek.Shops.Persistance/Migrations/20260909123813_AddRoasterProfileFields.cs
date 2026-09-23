using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddRoasterProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roasters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "About",
                table: "Roasters",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Roasters",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CityId",
                table: "Roasters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramLink",
                table: "Roasters",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAddressValidated",
                table: "Roasters",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Roasters",
                type: "numeric(18,10)",
                precision: 18,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Roasters",
                type: "numeric(18,10)",
                precision: 18,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModerationId",
                table: "Roasters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteLink",
                table: "Roasters",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoasterPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RoasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoasterPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoasterPhotos_Roasters_RoasterId",
                        column: x => x.RoasterId,
                        principalTable: "Roasters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoasterPhotos_RoasterId_SortIndex",
                table: "RoasterPhotos",
                columns: new[] { "RoasterId", "SortIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoasterPhotos");

            migrationBuilder.DropColumn(
                name: "About",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "InstagramLink",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "IsAddressValidated",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "ModerationId",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "SiteLink",
                table: "Roasters");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roasters",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
