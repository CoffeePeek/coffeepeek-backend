using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeeShop.Moderation.Persistence.Migrations;

[DbContext(typeof(ModerationDbContext))]
[Migration("20261007160000_RemoveLegacyReviewSnapshots")]
public sealed class RemoveLegacyReviewSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM "ModerationReviews") OR EXISTS (SELECT 1 FROM "ShopPhotos" WHERE "ModerationReviewId" IS NOT NULL) THEN
                IF to_regclass('"_LegacyCheckInMigration"') IS NULL THEN
                  RAISE EXCEPTION 'Historical moderation must be transferred using the previous release before this upgrade';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM "_LegacyCheckInMigration" WHERE "Completed") THEN
                  RAISE EXCEPTION 'Legacy moderation transfer is incomplete';
                END IF;
              END IF;
            END $$;
            DELETE FROM "ShopPhotos" WHERE "ModerationReviewId" IS NOT NULL
              AND "ModerationShopId" IS NULL AND "ModerationCheckInId" IS NULL;
            ALTER TABLE "ShopPhotos" DROP COLUMN "ModerationReviewId";
            DROP TABLE "ModerationReviews";
            DROP TABLE IF EXISTS "_LegacyCheckInMigration";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Legacy cleanup is irreversible; archived data was explicitly discarded.");
}
