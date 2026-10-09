using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class ProtectCityReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $preflight$
            DECLARE
                shop_orphans bigint;
                roaster_orphans bigint;
                zone_orphans bigint;
            BEGIN
                SELECT count(*) INTO shop_orphans
                FROM "Shops" AS s
                LEFT JOIN "Cities" AS c ON c."Id" = s."CityId"
                WHERE s."CityId" IS NOT NULL AND c."Id" IS NULL;

                SELECT count(*) INTO roaster_orphans
                FROM "Roasters" AS r
                LEFT JOIN "Cities" AS c ON c."Id" = r."CityId"
                WHERE r."CityId" IS NOT NULL AND c."Id" IS NULL;

                SELECT count(*) INTO zone_orphans
                FROM "CoffeeZones" AS z
                LEFT JOIN "Cities" AS c ON c."Id" = z."CityId"
                WHERE z."CityId" IS NOT NULL AND c."Id" IS NULL;

                IF shop_orphans + roaster_orphans + zone_orphans > 0 THEN
                    RAISE EXCEPTION
                        'City reference preflight failed: Shops %, Roasters %, CoffeeZones %. Run dev/check-city-references.sql and repair rows explicitly.',
                        shop_orphans, roaster_orphans, zone_orphans;
                END IF;
            END
            $preflight$;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Roasters_CityId",
            table: "Roasters",
            column: "CityId");

        migrationBuilder.AddForeignKey(
            name: "FK_Shops_Cities_CityId",
            table: "Shops",
            column: "CityId",
            principalTable: "Cities",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Roasters_Cities_CityId",
            table: "Roasters",
            column: "CityId",
            principalTable: "Cities",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Shops_Cities_CityId", "Shops");
        migrationBuilder.DropForeignKey("FK_Roasters_Cities_CityId", "Roasters");
        migrationBuilder.DropIndex("IX_Roasters_CityId", "Roasters");
    }
}
