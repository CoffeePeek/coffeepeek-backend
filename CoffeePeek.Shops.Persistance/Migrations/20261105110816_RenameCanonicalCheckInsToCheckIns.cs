using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class RenameCanonicalCheckInsToCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, "CanonicalCheckIns", "CheckIns", "CanonicalCheckIns", "CheckIns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, "CheckIns", "CanonicalCheckIns", "CheckIns", "CanonicalCheckIns");
        }

        private static void Rename(MigrationBuilder migrationBuilder, string oldTable, string newTable,
            string oldName, string newName)
        {
            var oldTriggerName = oldName == "CanonicalCheckIns" ? "canonical_checkins" : "checkins";
            var newTriggerName = newName == "CanonicalCheckIns" ? "canonical_checkins" : "checkins";
            migrationBuilder.Sql($"""
                ALTER TABLE "{oldTable}" RENAME TO "{newTable}";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "PK_{oldName}" TO "PK_{newName}";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "CK_{oldName}_CompleteContent" TO "CK_{newName}_CompleteContent";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "CK_{oldName}_Visibility" TO "CK_{newName}_Visibility";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "CK_{oldName}_KindShape" TO "CK_{newName}_KindShape";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "CK_{oldName}_HomeDetails" TO "CK_{newName}_HomeDetails";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "CK_{oldName}_HomeBeanSource" TO "CK_{newName}_HomeBeanSource";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "FK_{oldName}_Shops_ShopId" TO "FK_{newName}_Shops_ShopId";
                ALTER TABLE "{newTable}" RENAME CONSTRAINT "FK_{oldName}_CoffeeProducts_CoffeeProductId" TO "FK_{newName}_CoffeeProducts_CoffeeProductId";
                ALTER TABLE "CheckInHelpfulVotes" RENAME CONSTRAINT "FK_CheckInHelpfulVotes_{oldName}_CheckInId" TO "FK_CheckInHelpfulVotes_{newName}_CheckInId";
                ALTER TABLE "CheckInPhotos" RENAME CONSTRAINT "FK_CheckInPhotos_{oldName}_CanonicalCheckInId" TO "FK_CheckInPhotos_{newName}_CanonicalCheckInId";
                ALTER INDEX "IX_{oldName}_CoffeeProductId" RENAME TO "IX_{newName}_CoffeeProductId";
                ALTER INDEX "IX_{oldName}_FirstPublishedAtUtc_Id" RENAME TO "IX_{newName}_FirstPublishedAtUtc_Id";
                ALTER INDEX "IX_{oldName}_ShopId" RENAME TO "IX_{newName}_ShopId";
                ALTER INDEX "IX_{oldName}_UserId" RENAME TO "IX_{newName}_UserId";
                ALTER INDEX "IX_{oldName}_UserId_Kind_CoffeeProductId" RENAME TO "IX_{newName}_UserId_Kind_CoffeeProductId";
                ALTER INDEX "IX_{oldName}_UserId_Kind_PreparedOn_VisitedAt_Id" RENAME TO "IX_{newName}_UserId_Kind_PreparedOn_VisitedAt_Id";
                ALTER INDEX "IX_{oldName}_UserId_ShelfBagId" RENAME TO "IX_{newName}_UserId_ShelfBagId";
                ALTER INDEX "IX_{oldName}_UserId_ShopId" RENAME TO "IX_{newName}_UserId_ShopId";
                ALTER INDEX "IX_{oldName}_UserId_ShopId_VisitedAt_CreatedAtUtc_Id" RENAME TO "IX_{newName}_UserId_ShopId_VisitedAt_CreatedAtUtc_Id";
                ALTER INDEX "IX_{oldName}_Visibility_ModerationState_CreatedAtUtc_Id" RENAME TO "IX_{newName}_Visibility_ModerationState_CreatedAtUtc_Id";
                ALTER TRIGGER "trg_{oldTriggerName}_completeness" ON "{newTable}" RENAME TO "trg_{newTriggerName}_completeness";
                """);

            migrationBuilder.Sql($"""
                CREATE OR REPLACE FUNCTION cp_calculate_shop_data_completeness(shop_id uuid)
                RETURNS smallint
                LANGUAGE sql
                STABLE
                AS $function$
                    SELECT (
                        CASE WHEN EXISTS (SELECT 1 FROM "ShopPhotos" p WHERE p."CoffeeShopId" = s."Id") THEN 15 ELSE 0 END
                        + CASE WHEN EXISTS (
                            SELECT 1 FROM "ShopMenus" m WHERE m."CoffeeShopId" = s."Id"
                              AND (EXISTS (SELECT 1 FROM "ShopMenuItems" mi WHERE mi."ShopMenuId" = m."Id" AND mi."Availability" = 1)
                                   OR EXISTS (SELECT 1 FROM "ShopMenuPhotos" mp WHERE mp."ShopMenuId" = m."Id"))
                          ) THEN 15 ELSE 0 END
                        + CASE WHEN EXISTS (
                            SELECT 1 FROM "{newTable}" r WHERE r."ShopId" = s."Id" AND r."DeletedAtUtc" IS NULL
                              AND r."Visibility" = 1 AND r."ModerationState" = 2 AND r."ApprovedRevision" = r."ContentRevision"
                          ) THEN 10 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "ShopSchedule" sch WHERE sch."CoffeeShopId" = s."Id") THEN 15 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "CoffeeShopRoasters" cr WHERE cr."CoffeeShopId" = s."Id") THEN 5 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "CoffeeShopBrewMethods" cbm WHERE cbm."CoffeeShopId" = s."Id") THEN 5 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "CoffeeShopCoffeeBeans" cb WHERE cb."CoffeeShopId" = s."Id") THEN 5 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "CoffeeShopEquipments" ce WHERE ce."CoffeeShopId" = s."Id") THEN 5 ELSE 0 END
                        + CASE WHEN NULLIF(BTRIM(s."InstagramLink"), '') IS NOT NULL THEN 5 ELSE 0 END
                        + CASE WHEN NULLIF(BTRIM(s."PhoneNumber"), '') IS NOT NULL THEN 5 ELSE 0 END
                        + CASE WHEN NULLIF(BTRIM(s."SiteLink"), '') IS NOT NULL THEN 5 ELSE 0 END
                        + CASE WHEN EXISTS (SELECT 1 FROM "{newTable}" ci WHERE ci."ShopId" = s."Id" AND ci."DeletedAtUtc" IS NULL) THEN 10 ELSE 0 END
                    )::smallint
                    FROM "Shops" s
                    WHERE s."Id" = shop_id;
                $function$;
                """);
        }
    }
}
