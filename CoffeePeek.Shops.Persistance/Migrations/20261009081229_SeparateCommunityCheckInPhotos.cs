using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCommunityCheckInPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "ShopPhotos" IN SHARE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "ShopPhotos" p JOIN "CanonicalCheckIns" c ON c."Id"=p."CanonicalCheckInId"
                        WHERE p."OwnerId"<>c."UserId" OR p."CoffeeShopId" IS NOT NULL
                           OR p."SizeBytes"<=0 OR p."SortIndex"<0
                           OR length(btrim(p."FileName"))=0 OR length(btrim(p."ContentType"))=0
                           OR length(btrim(p."StorageKey"))=0)
                        OR EXISTS (SELECT 1 FROM "ShopPhotos" WHERE "CanonicalCheckInId" IS NOT NULL
                            GROUP BY "CanonicalCheckInId" HAVING count(*)>5)
                        OR EXISTS (SELECT 1 FROM "ShopPhotos" WHERE "CanonicalCheckInId" IS NOT NULL
                            GROUP BY "StorageKey" HAVING count(*)>1)
                    THEN
                        RAISE EXCEPTION 'Canonical check-in photos require explicit repair: foreign owner, shared gallery, invalid metadata, duplicates or more than five photos.';
                    END IF;
                END $$;
                """);
            migrationBuilder.CreateTable(
                name: "CheckInPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CanonicalCheckInId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInPhotos_CanonicalCheckIns_CanonicalCheckInId",
                        column: x => x.CanonicalCheckInId,
                        principalTable: "CanonicalCheckIns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInPhotos_CanonicalCheckInId_SortIndex",
                table: "CheckInPhotos",
                columns: new[] { "CanonicalCheckInId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInPhotos_StorageKey",
                table: "CheckInPhotos",
                column: "StorageKey",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "CheckInPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId",
                    "SortIndex", "CanonicalCheckInId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT "Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId",
                    "SortIndex", "CanonicalCheckInId", "CreatedAtUtc", "UpdatedAtUtc"
                FROM "ShopPhotos" WHERE "CanonicalCheckInId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Check-in photos may have new writes. Drain writers and use a reconciled forward fix; dropping this table would lose data.");
        }
    }
}
