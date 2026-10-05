using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveUserSlugToUserTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "SlugRevision",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
            UPDATE "Users" o SET "Slug"=a."Slug", "SlugRevision"=a."Revision"
            FROM "UserPublicAddresses" a WHERE a."OwnerId"=o."Id" AND a."State"=1 AND NOT o."IsSoftDelete";
            
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
                FOR owner IN SELECT "Id", "Username" AS stem, "Slug" AS old_slug FROM "Users"
                    WHERE ("Slug" IS NULL) AND NOT "IsSoftDelete" ORDER BY "Id" LOOP
                    IF pg_temp.cp_slug(owner.stem) IN ('place','user') THEN stem:='user'; attempt:=1; ELSE stem:=owner.stem; attempt:=0; END IF;
                    LOOP
                        candidate:=pg_temp.cp_slug_candidate(stem,owner."Id",attempt);
                        EXIT WHEN NOT EXISTS (SELECT 1 FROM "Users" WHERE "Slug"=candidate AND "Id"<>owner."Id");
                        attempt:=attempt+1;
                        IF attempt>=6 THEN RAISE EXCEPTION 'Cannot allocate unique slug for %',owner."Id"; END IF;
                    END LOOP;
                    UPDATE "Users" SET "Slug"=candidate,"SlugRevision"=CASE WHEN owner.old_slug IS NULL THEN 1 ELSE "SlugRevision"+1 END
                        WHERE "Id"=owner."Id";
                END LOOP;
            END $backfill$;
            
            DROP FUNCTION pg_temp.cp_slug_candidate(text,uuid,integer);
            DROP FUNCTION pg_temp.cp_slug(text);
            """);

            migrationBuilder.DropTable(
                name: "UserPublicAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Slug",
                table: "Users",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PublicSlug",
                table: "Users",
                sql: "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Address history was discarded. Restore a database backup to downgrade.");
    }
}
