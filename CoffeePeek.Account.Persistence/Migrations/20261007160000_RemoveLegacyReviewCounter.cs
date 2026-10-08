using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.Account.Persistence.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20261007160000_RemoveLegacyReviewCounter")]
public sealed class RemoveLegacyReviewCounter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM "Users" WHERE "ReviewCount"<>0 OR "CheckInCount"<>0) THEN
                IF to_regclass('"_LegacyCheckInMigration"') IS NULL THEN
                  RAISE EXCEPTION 'Run CoffeePeek.LegacyCheckInMigration to recalculate check-in counters';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM "_LegacyCheckInMigration" WHERE "Completed") THEN
                  RAISE EXCEPTION 'Legacy counter transfer is incomplete';
                END IF;
              END IF;
            END $$;
            ALTER TABLE "Users" DROP COLUMN "ReviewCount";
            DROP TABLE IF EXISTS "_LegacyCheckInMigration";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Legacy cleanup is irreversible; archived counters were explicitly discarded.");
}
