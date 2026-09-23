using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddShopPhotoSortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopPhotos_CoffeeShopId",
                table: "ShopPhotos");

            migrationBuilder.AddColumn<int>(
                name: "SortIndex",
                table: "ShopPhotos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "ShopPhotos" AS sp
                SET "SortIndex" = ranked.rn
                FROM (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "CoffeeShopId"
                               ORDER BY "CreatedAtUtc", "Id"
                           ) - 1 AS rn
                    FROM "ShopPhotos"
                    WHERE "CoffeeShopId" IS NOT NULL
                ) AS ranked
                WHERE sp."Id" = ranked."Id";

                UPDATE "ShopPhotos" AS sp
                SET "SortIndex" = ranked.rn
                FROM (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "CheckInId"
                               ORDER BY "CreatedAtUtc", "Id"
                           ) - 1 AS rn
                    FROM "ShopPhotos"
                    WHERE "CheckInId" IS NOT NULL
                ) AS ranked
                WHERE sp."Id" = ranked."Id";

                UPDATE "ShopPhotos" AS sp
                SET "SortIndex" = ranked.rn
                FROM (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "ReviewId"
                               ORDER BY "CreatedAtUtc", "Id"
                           ) - 1 AS rn
                    FROM "ShopPhotos"
                    WHERE "ReviewId" IS NOT NULL
                ) AS ranked
                WHERE sp."Id" = ranked."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ShopPhotos_CoffeeShopId_SortIndex",
                table: "ShopPhotos",
                columns: new[] { "CoffeeShopId", "SortIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopPhotos_CoffeeShopId_SortIndex",
                table: "ShopPhotos");

            migrationBuilder.DropColumn(
                name: "SortIndex",
                table: "ShopPhotos");

            migrationBuilder.CreateIndex(
                name: "IX_ShopPhotos_CoffeeShopId",
                table: "ShopPhotos",
                column: "CoffeeShopId");
        }
    }
}
