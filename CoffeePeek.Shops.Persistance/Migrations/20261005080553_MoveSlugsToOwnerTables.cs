using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class MoveSlugsToOwnerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicSlug",
                table: "ShopTags",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "ShopTags",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "Shops",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Roasters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "Roasters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Equipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "Equipments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "CoffeeZones",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "CoffeeZones",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "CoffeeBeans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "CoffeeBeans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Cities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "Cities",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "BrewMethods",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "BrewMethods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
            UPDATE "Shops" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "CoffeeShopPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "Roasters" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "RoasterPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "Cities" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "CityPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "CoffeeZones" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "CoffeeZonePublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "CoffeeBeans" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "CoffeeBeanPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "Equipments" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "EquipmentPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "BrewMethods" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "BrewMethodPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            UPDATE "ShopTags" o SET "PublicSlug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "ShopTagPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1;
            
            CREATE OR REPLACE FUNCTION pg_temp.cp_slug(name text) RETURNS text LANGUAGE plpgsql AS $fn$
            DECLARE input text := lower(coalesce(name, '')); output text := ''; c text; result text;
            BEGIN
                input := translate(input, 'àáâãäåāăąçćčďèéêëēĕėęěìíîïīĭįñńňòóôõöōŏőŕřśšşťùúûüūŭůűųýÿžźż',
                                          'aaaaaaaaacccdeeeeeeeeeiiiiiiinnnoooooooorrssstuuuuuuuuuyyzzz');
                FOR i IN 1..length(input) LOOP
                    c := substr(input, i, 1);
                    output := output || CASE c
                        WHEN 'а' THEN 'a'
                        WHEN 'б' THEN 'b'
                        WHEN 'в' THEN 'v'
                        WHEN 'г' THEN 'g'
                        WHEN 'ґ' THEN 'g'
                        WHEN 'д' THEN 'd'
                        WHEN 'е' THEN 'e'
                        WHEN 'ё' THEN 'yo'
                        WHEN 'ж' THEN 'zh'
                        WHEN 'з' THEN 'z'
                        WHEN 'и' THEN 'i'
                        WHEN 'і' THEN 'i'
                        WHEN 'й' THEN 'y'
                        WHEN 'к' THEN 'k'
                        WHEN 'л' THEN 'l'
                        WHEN 'м' THEN 'm'
                        WHEN 'н' THEN 'n'
                        WHEN 'о' THEN 'o'
                        WHEN 'п' THEN 'p'
                        WHEN 'р' THEN 'r'
                        WHEN 'с' THEN 's'
                        WHEN 'т' THEN 't'
                        WHEN 'у' THEN 'u'
                        WHEN 'ў' THEN 'u'
                        WHEN 'ф' THEN 'f'
                        WHEN 'х' THEN 'kh'
                        WHEN 'ц' THEN 'ts'
                        WHEN 'ч' THEN 'ch'
                        WHEN 'ш' THEN 'sh'
                        WHEN 'щ' THEN 'shch'
                        WHEN 'ъ' THEN ''
                        WHEN 'ь' THEN ''
                        WHEN 'ы' THEN 'y'
                        WHEN 'э' THEN 'e'
                        WHEN 'ю' THEN 'yu'
                        WHEN 'я' THEN 'ya'
                        WHEN 'ß' THEN 'ss'
                        WHEN 'æ' THEN 'ae'
                        WHEN 'œ' THEN 'oe'
                        WHEN 'ø' THEN 'o'
                        WHEN 'ł' THEN 'l'
                        WHEN '''' THEN ''
                        WHEN '’' THEN ''
                        ELSE c END;
                END LOOP;
                result := trim(both '-' from left(trim(both '-' from regexp_replace(output, '[^a-z0-9]+', '-', 'g')), 100));
                IF result = '' OR result = ANY (ARRAY['api','admin','new','edit','create','delete','search','me','by-slug','login','logout','register','settings','health','swagger','scalar','null','undefined','public-addresses','history','backfill','initialize','rename','batch','visibility','owner','photos','menu'])
                   OR result ~ '^[0-9a-f]{32}$|^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
                    RETURN 'place';
                END IF;
                RETURN result;
            END $fn$;
            
            CREATE OR REPLACE FUNCTION pg_temp.cp_slug_candidate(stem text, id uuid, attempt integer) RETURNS text LANGUAGE plpgsql AS $fn$
            DECLARE compact text := replace(id::text, '-', ''); identity text; suffix text;
            BEGIN
                IF attempt = 0 THEN RETURN pg_temp.cp_slug(stem); END IF;
                -- Match Guid.ToByteArray(): the first three UUID fields are little-endian.
                identity := left(encode(sha256(decode(substr(compact,7,2)||substr(compact,5,2)||substr(compact,3,2)||substr(compact,1,2)||
                    substr(compact,11,2)||substr(compact,9,2)||substr(compact,15,2)||substr(compact,13,2)||substr(compact,17), 'hex')), 'hex'), 32);
                suffix := '-' || CASE attempt WHEN 1 THEN left(identity,8) WHEN 2 THEN left(identity,12) WHEN 3 THEN left(identity,16)
                    ELSE identity || '-' || attempt::text END;
                RETURN rtrim(left(pg_temp.cp_slug(stem),100-length(suffix)),'-') || suffix;
            END $fn$;
            
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" || ' ' || coalesce("Address", '') AS stem, "Slug" AS old_slug FROM "Shops"
                    WHERE ("Slug" IS NULL OR "Slug" ~ '^[a-z]+-[0-9a-f-]{16,36}$') ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "Shops" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "Shops" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "Roasters"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "Roasters" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "Roasters" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "Cities"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "Cities" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "Cities" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "CoffeeZones"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "CoffeeZones" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "CoffeeZones" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "CoffeeBeans"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "CoffeeBeans" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "CoffeeBeans" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "Equipments"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "Equipments" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "Equipments" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Name" AS stem, "Slug" AS old_slug FROM "BrewMethods"
                    WHERE ("Slug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "BrewMethods" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "BrewMethods" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DO $backfill$
            DECLARE owner record; stem text; candidate text; attempt integer;
            BEGIN
                FOR owner IN SELECT "Id", "Slug" AS stem, "PublicSlug" AS old_slug FROM "ShopTags"
                    WHERE ("PublicSlug" IS NULL) ORDER BY "Id" LOOP
                    stem:=owner.stem; attempt:=0;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "ShopTags" WHERE "PublicSlug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "ShopTags" SET "PublicSlug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DROP FUNCTION pg_temp.cp_slug_candidate(text,uuid,integer);
            DROP FUNCTION pg_temp.cp_slug(text);
            """);

            migrationBuilder.DropTable(
                name: "BrewMethodPublicAddresses");

            migrationBuilder.DropTable(
                name: "CityPublicAddresses");

            migrationBuilder.DropTable(
                name: "CoffeeBeanPublicAddresses");

            migrationBuilder.DropTable(
                name: "CoffeeShopPublicAddresses");

            migrationBuilder.DropTable(
                name: "CoffeeZonePublicAddresses");

            migrationBuilder.DropTable(
                name: "EquipmentPublicAddresses");

            migrationBuilder.DropTable(
                name: "PublicAddressAudit");

            migrationBuilder.DropTable(
                name: "RoasterPublicAddresses");

            migrationBuilder.DropTable(
                name: "ShopTagPublicAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_ShopTags_PublicSlug",
                table: "ShopTags",
                column: "PublicSlug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShopTags_PublicSlug",
                table: "ShopTags",
                sql: "(\"PublicSlug\" IS NULL AND \"SlugRevision\" = 0) OR (\"PublicSlug\" IS NOT NULL AND \"PublicSlug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"PublicSlug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Slug",
                table: "Shops",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Shops_PublicSlug",
                table: "Shops",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_Roasters_Slug",
                table: "Roasters",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Roasters_PublicSlug",
                table: "Roasters",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_Slug",
                table: "Equipments",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Equipments_PublicSlug",
                table: "Equipments",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeZones_Slug",
                table: "CoffeeZones",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CoffeeZones_PublicSlug",
                table: "CoffeeZones",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeBeans_Slug",
                table: "CoffeeBeans",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CoffeeBeans_PublicSlug",
                table: "CoffeeBeans",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Slug",
                table: "Cities",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Cities_PublicSlug",
                table: "Cities",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_BrewMethods_Slug",
                table: "BrewMethods",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BrewMethods_PublicSlug",
                table: "BrewMethods",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Address history was discarded. Restore a database backup to downgrade.");
    }
}
