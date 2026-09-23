using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationRoaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationRoasters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    About = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ModerationStatus = table.Column<int>(type: "integer", nullable: false),
                    RejectedReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Contact_InstagramLink = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Contact_SiteLink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Location_Address = table.Column<string>(type: "text", nullable: true),
                    Location_IsAddressValidated = table.Column<bool>(type: "boolean", nullable: true),
                    Location_Latitude = table.Column<decimal>(type: "numeric", nullable: true),
                    Location_Longitude = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationRoasters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModerationRoasterPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModerationRoasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationRoasterPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationRoasterPhotos_ModerationRoasters_ModerationRoaste~",
                        column: x => x.ModerationRoasterId,
                        principalTable: "ModerationRoasters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasterPhotos_ModerationRoasterId",
                table: "ModerationRoasterPhotos",
                column: "ModerationRoasterId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasters_Location_Latitude_Location_Longitude",
                table: "ModerationRoasters",
                columns: new[] { "Location_Latitude", "Location_Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasters_ModerationStatus",
                table: "ModerationRoasters",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasters_UserId",
                table: "ModerationRoasters",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationRoasterPhotos");

            migrationBuilder.DropTable(
                name: "ModerationRoasters");
        }
    }
}
