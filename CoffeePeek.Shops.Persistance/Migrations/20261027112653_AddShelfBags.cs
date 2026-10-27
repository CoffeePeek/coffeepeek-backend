using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfBags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShelfBags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: true),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhotoStorageKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PhotoContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CoffeeProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    RoasterName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    WeightGrams = table.Column<int>(type: "integer", nullable: true),
                    Grind = table.Column<int>(type: "integer", nullable: true),
                    RoastedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    BuyAgain = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShelfBags", x => x.Id);
                    table.CheckConstraint("CK_ShelfBags_Grind", "\"Grind\" IS NULL OR \"Grind\" IN (0, 1)");
                    table.CheckConstraint("CK_ShelfBags_Photo", "(\"PhotoId\" IS NULL) = (\"PhotoStorageKey\" IS NULL) AND (\"PhotoId\" IS NULL) = (\"PhotoContentType\" IS NULL)");
                    table.CheckConstraint("CK_ShelfBags_PhotoOrBean", "\"PhotoId\" IS NOT NULL OR \"Name\" IS NOT NULL");
                    table.CheckConstraint("CK_ShelfBags_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ShelfBags_StatusPosition", "(\"Status\" = 0 AND \"Position\" IS NOT NULL AND \"Position\" >= 0 AND \"FinishedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"Position\" IS NULL AND \"FinishedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_ShelfBags_Weight", "\"WeightGrams\" IS NULL OR \"WeightGrams\" BETWEEN 1 AND 10000");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShelfBags_PhotoId",
                table: "ShelfBags",
                column: "PhotoId",
                unique: true,
                filter: "\"PhotoId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShelfBags_UserId_Status_FinishedAtUtc_Id",
                table: "ShelfBags",
                columns: new[] { "UserId", "Status", "FinishedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ShelfBags_UserId_Status_Position",
                table: "ShelfBags",
                columns: new[] { "UserId", "Status", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShelfBags");
        }
    }
}
