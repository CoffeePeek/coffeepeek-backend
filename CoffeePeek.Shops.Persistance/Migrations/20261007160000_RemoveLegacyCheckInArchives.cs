using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Shops.Persistance.Migrations;

[DbContext(typeof(ShopsDbContext))]
[Migration("20261007160000_RemoveLegacyCheckInArchives")]
public sealed class RemoveLegacyCheckInArchives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM "CheckIns") OR EXISTS (SELECT 1 FROM "Reviews")
                 OR EXISTS (SELECT 1 FROM "ShopPhotos" WHERE "CheckInId" IS NOT NULL OR "ReviewId" IS NOT NULL) THEN
                IF to_regclass('"_LegacyCheckInMigration"') IS NULL THEN
                  RAISE EXCEPTION 'Historical check-ins must be transferred using the previous release before this upgrade';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM "_LegacyCheckInMigration" WHERE "Completed") THEN
                  RAISE EXCEPTION 'Legacy check-in transfer is incomplete';
                END IF;
              END IF;
            END $$;
            CREATE OR REPLACE FUNCTION cp_calculate_shop_data_completeness(shop_id uuid)
            RETURNS smallint
            LANGUAGE sql
            STABLE
            AS $function$
                SELECT (
                    CASE WHEN EXISTS (
                        SELECT 1 FROM "ShopPhotos" p
                        WHERE p."CoffeeShopId" = s."Id"
                    ) THEN 15 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1
                        FROM "ShopMenus" m
                        WHERE m."CoffeeShopId" = s."Id"
                          AND (
                              EXISTS (
                                  SELECT 1 FROM "ShopMenuItems" mi
                                  WHERE mi."ShopMenuId" = m."Id" AND mi."Availability" = 1
                              )
                              OR EXISTS (SELECT 1 FROM "ShopMenuPhotos" mp WHERE mp."ShopMenuId" = m."Id")
                          )
                    ) THEN 15 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CanonicalCheckIns" r
                        WHERE r."ShopId" = s."Id" AND r."DeletedAtUtc" IS NULL
                          AND r."Visibility"=1 AND r."ModerationState"=2 AND r."ApprovedRevision"=r."ContentRevision"
                    ) THEN 10 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "ShopSchedule" sch
                        WHERE sch."CoffeeShopId" = s."Id"
                    ) THEN 15 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CoffeeShopRoasters" cr
                        WHERE cr."CoffeeShopId" = s."Id"
                    ) THEN 5 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CoffeeShopBrewMethods" cbm
                        WHERE cbm."CoffeeShopId" = s."Id"
                    ) THEN 5 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CoffeeShopCoffeeBeans" cb
                        WHERE cb."CoffeeShopId" = s."Id"
                    ) THEN 5 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CoffeeShopEquipments" ce
                        WHERE ce."CoffeeShopId" = s."Id"
                    ) THEN 5 ELSE 0 END
                    + CASE WHEN NULLIF(BTRIM(s."InstagramLink"), '') IS NOT NULL THEN 5 ELSE 0 END
                    + CASE WHEN NULLIF(BTRIM(s."PhoneNumber"), '') IS NOT NULL THEN 5 ELSE 0 END
                    + CASE WHEN NULLIF(BTRIM(s."SiteLink"), '') IS NOT NULL THEN 5 ELSE 0 END
                    + CASE WHEN EXISTS (
                        SELECT 1 FROM "CanonicalCheckIns" ci
                        WHERE ci."ShopId" = s."Id" AND ci."DeletedAtUtc" IS NULL
                    ) THEN 10 ELSE 0 END
                )::smallint
                FROM "Shops" s
                WHERE s."Id" = shop_id;
            $function$;

            CREATE TRIGGER trg_canonical_checkins_completeness
              AFTER INSERT OR UPDATE OR DELETE ON "CanonicalCheckIns"
              FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();
            DELETE FROM "ShopPhotos" WHERE ("CheckInId" IS NOT NULL OR "ReviewId" IS NOT NULL)
              AND "CoffeeShopId" IS NULL AND "CanonicalCheckInId" IS NULL;
            ALTER TABLE "ShopPhotos" DROP COLUMN "CheckInId", DROP COLUMN "ReviewId";
            DROP TABLE "ReviewHelpfulVotes";
            DROP TABLE "CheckIns";
            DROP TABLE "Reviews";
            SELECT cp_refresh_shop_data_completeness("Id") FROM "Shops";
            DROP TABLE IF EXISTS "_LegacyCheckInMigration";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Legacy cleanup is irreversible; archived data was explicitly discarded.");
}
