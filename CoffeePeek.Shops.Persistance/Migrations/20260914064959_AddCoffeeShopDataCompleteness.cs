using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeShopDataCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "DataCompletenessScore",
                table: "Shops",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Status_DataCompletenessScore_Name",
                table: "Shops",
                columns: new[] { "Status", "DataCompletenessScore", "Name" },
                descending: new[] { false, true, false });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Shops_DataCompletenessScore_Range",
                table: "Shops",
                sql: "\"DataCompletenessScore\" BETWEEN 0 AND 100");

            migrationBuilder.Sql(
                """
                CREATE FUNCTION cp_calculate_shop_data_completeness(shop_id uuid)
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
                            SELECT 1 FROM "Reviews" r
                            WHERE r."CoffeeShopId" = s."Id" AND NOT r."IsSoftDelete"
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
                            SELECT 1 FROM "CheckIns" ci
                            WHERE ci."ShopId" = s."Id"
                        ) THEN 10 ELSE 0 END
                    )::smallint
                    FROM "Shops" s
                    WHERE s."Id" = shop_id;
                $function$;

                CREATE FUNCTION cp_refresh_shop_data_completeness(shop_id uuid)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF shop_id IS NULL THEN
                        RETURN;
                    END IF;

                    UPDATE "Shops"
                    SET "DataCompletenessScore" = COALESCE(
                        cp_calculate_shop_data_completeness(shop_id),
                        0)
                    WHERE "Id" = shop_id;
                END;
                $function$;

                CREATE FUNCTION cp_refresh_shop_from_shop_row()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    PERFORM cp_refresh_shop_data_completeness(NEW."Id");
                    RETURN NEW;
                END;
                $function$;

                CREATE FUNCTION cp_refresh_shop_from_direct_relation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    old_shop_id uuid;
                    new_shop_id uuid;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        old_shop_id := COALESCE(
                            (to_jsonb(OLD) ->> 'CoffeeShopId')::uuid,
                            (to_jsonb(OLD) ->> 'ShopId')::uuid);
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        new_shop_id := COALESCE(
                            (to_jsonb(NEW) ->> 'CoffeeShopId')::uuid,
                            (to_jsonb(NEW) ->> 'ShopId')::uuid);
                    END IF;

                    PERFORM cp_refresh_shop_data_completeness(old_shop_id);
                    IF new_shop_id IS DISTINCT FROM old_shop_id THEN
                        PERFORM cp_refresh_shop_data_completeness(new_shop_id);
                    END IF;

                    -- Return value is ignored for AFTER triggers.
                    RETURN NULL;
                END;
                $function$;

                CREATE FUNCTION cp_refresh_shop_from_menu_content()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    old_menu_id uuid;
                    new_menu_id uuid;
                    old_shop_id uuid;
                    new_shop_id uuid;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        old_menu_id := (to_jsonb(OLD) ->> 'ShopMenuId')::uuid;
                        SELECT "CoffeeShopId" INTO old_shop_id
                        FROM "ShopMenus" WHERE "Id" = old_menu_id;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        new_menu_id := (to_jsonb(NEW) ->> 'ShopMenuId')::uuid;
                        SELECT "CoffeeShopId" INTO new_shop_id
                        FROM "ShopMenus" WHERE "Id" = new_menu_id;
                    END IF;

                    PERFORM cp_refresh_shop_data_completeness(old_shop_id);
                    IF new_shop_id IS DISTINCT FROM old_shop_id THEN
                        PERFORM cp_refresh_shop_data_completeness(new_shop_id);
                    END IF;

                    -- Return value is ignored for AFTER triggers.
                    RETURN NULL;
                END;
                $function$;

                CREATE TRIGGER trg_shops_completeness_after_insert
                AFTER INSERT ON "Shops"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_shop_row();

                CREATE TRIGGER trg_shops_completeness_after_contact_update
                AFTER UPDATE OF "InstagramLink", "PhoneNumber", "SiteLink" ON "Shops"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_shop_row();

                CREATE TRIGGER trg_shop_photos_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "ShopPhotos"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_menus_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "ShopMenus"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_menu_items_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "ShopMenuItems"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_menu_content();

                CREATE TRIGGER trg_shop_menu_photos_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "ShopMenuPhotos"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_menu_content();

                CREATE TRIGGER trg_reviews_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "Reviews"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_check_ins_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "CheckIns"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_schedule_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "ShopSchedule"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_roasters_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "CoffeeShopRoasters"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_brew_methods_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "CoffeeShopBrewMethods"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_coffee_beans_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "CoffeeShopCoffeeBeans"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                CREATE TRIGGER trg_shop_equipments_completeness
                AFTER INSERT OR UPDATE OR DELETE ON "CoffeeShopEquipments"
                FOR EACH ROW EXECUTE FUNCTION cp_refresh_shop_from_direct_relation();

                UPDATE "Shops" s
                SET "DataCompletenessScore" = cp_calculate_shop_data_completeness(s."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_shops_completeness_after_insert ON "Shops";
                DROP TRIGGER IF EXISTS trg_shops_completeness_after_contact_update ON "Shops";
                DROP TRIGGER IF EXISTS trg_shop_photos_completeness ON "ShopPhotos";
                DROP TRIGGER IF EXISTS trg_shop_menus_completeness ON "ShopMenus";
                DROP TRIGGER IF EXISTS trg_shop_menu_items_completeness ON "ShopMenuItems";
                DROP TRIGGER IF EXISTS trg_shop_menu_photos_completeness ON "ShopMenuPhotos";
                DROP TRIGGER IF EXISTS trg_reviews_completeness ON "Reviews";
                DROP TRIGGER IF EXISTS trg_check_ins_completeness ON "CheckIns";
                DROP TRIGGER IF EXISTS trg_shop_schedule_completeness ON "ShopSchedule";
                DROP TRIGGER IF EXISTS trg_shop_roasters_completeness ON "CoffeeShopRoasters";
                DROP TRIGGER IF EXISTS trg_shop_brew_methods_completeness ON "CoffeeShopBrewMethods";
                DROP TRIGGER IF EXISTS trg_shop_coffee_beans_completeness ON "CoffeeShopCoffeeBeans";
                DROP TRIGGER IF EXISTS trg_shop_equipments_completeness ON "CoffeeShopEquipments";

                DROP FUNCTION IF EXISTS cp_refresh_shop_from_menu_content();
                DROP FUNCTION IF EXISTS cp_refresh_shop_from_direct_relation();
                DROP FUNCTION IF EXISTS cp_refresh_shop_from_shop_row();
                DROP FUNCTION IF EXISTS cp_refresh_shop_data_completeness(uuid);
                DROP FUNCTION IF EXISTS cp_calculate_shop_data_completeness(uuid);
                """);

            migrationBuilder.DropIndex(
                name: "IX_Shops_Status_DataCompletenessScore_Name",
                table: "Shops");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Shops_DataCompletenessScore_Range",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "DataCompletenessScore",
                table: "Shops");
        }
    }
}
