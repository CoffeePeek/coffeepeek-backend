using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations;

public partial class RemoveLegacyImportSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Retain imported candidates and their published shops as file imports. Value 3 is retired.
        migrationBuilder.Sql("""
            UPDATE "ShopImportCandidates" legacy
            SET "ExternalId" = 'legacy-' || legacy."Id"::text
            WHERE legacy."Source" = 3 AND EXISTS (
                SELECT 1 FROM "ShopImportCandidates" file
                WHERE file."Source" = 2 AND file."ExternalId" = legacy."ExternalId");

            UPDATE "ShopImportCandidates" legacy
            SET "Source" = 2,
                "Signals" = coalesce((
                    SELECT jsonb_agg(DISTINCT CASE
                        WHEN signal LIKE 'name:%' OR signal LIKE 'osm:%' OR signal LIKE 'google:%' OR signal LIKE 'import:%' THEN signal
                        WHEN position(':' in signal) > 0 THEN 'import:' || substring(signal from position(':' in signal) + 1)
                        ELSE 'import:file' END)
                    FROM jsonb_array_elements_text(legacy."Signals") AS value(signal)
                ), '[]'::jsonb)
            WHERE legacy."Source" = 3;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retired import provenance is not retained. Restore a database backup to downgrade.");
}