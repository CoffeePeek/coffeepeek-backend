using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurgeSoftDeletedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Public addresses have a RESTRICT FK to Users. Keep reserved slugs as anonymous
            // tombstones before removing accounts that were deleted by the old handlers.
            migrationBuilder.Sql("""
                WITH retiring AS (
                    SELECT a."OwnerId", MAX(a."Revision") + 1 AS "NextRevision"
                    FROM "UserPublicAddresses" AS a
                    JOIN "Users" AS u ON u."Id" = a."OwnerId"
                    WHERE u."IsSoftDelete"
                    GROUP BY a."OwnerId"
                )
                UPDATE "UserPublicAddresses" AS a
                SET "OwnerId" = NULL,
                    "State" = 3,
                    "Revision" = retiring."NextRevision",
                    "UpdatedAtUtc" = GREATEST(a."UpdatedAtUtc", CURRENT_TIMESTAMP)
                FROM retiring
                WHERE a."OwnerId" = retiring."OwnerId";
                """);

            migrationBuilder.Sql("DELETE FROM \"Users\" WHERE \"IsSoftDelete\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted account data cannot be reconstructed.
        }
    }
}
