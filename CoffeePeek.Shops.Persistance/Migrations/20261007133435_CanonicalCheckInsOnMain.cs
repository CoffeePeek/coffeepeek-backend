using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalCheckInsOnMain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing main visits/reviews/photos remain physical archives outside the runtime model.
            // Only this obsolete report FK is detached so new reports can reference canonical visits.
            migrationBuilder.DropForeignKey(name: "FK_ReviewReports_Reviews_ReviewId", table: "ReviewReports");
            migrationBuilder.AddColumn<Guid>(name: "CanonicalCheckInId", table: "ShopPhotos", type: "uuid", nullable: true);
            migrationBuilder.AlterColumn<string>(name: "StorageKey", table: "ShopPhotos", type: "character varying(255)", maxLength: 255,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200);
            migrationBuilder.AlterColumn<string>(name: "FileName", table: "ShopPhotos", type: "character varying(255)", maxLength: 255,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50);
            migrationBuilder.AlterColumn<string>(name: "ContentType", table: "ShopPhotos", type: "character varying(100)", maxLength: 100,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30);
            // DDL generated from the current Npgsql EF model; no legacy table rename or data copy.
            migrationBuilder.Sql("""
                CREATE TABLE "CanonicalCheckIns" (
                    "Id" uuid NOT NULL,
                    "DrinkSlug" character varying(50),
                    "CustomDrinkName" character varying(100),
                    "DrinkNameRu" character varying(100),
                    "DrinkNameEn" character varying(100),
                    "Note" character varying(1000) NOT NULL,
                    "UserId" uuid NOT NULL,
                    "ShopId" uuid NOT NULL,
                    "VisitedAt" timestamp with time zone NOT NULL,
                    "Username" character varying(30) NOT NULL DEFAULT '',
                    "Visibility" integer NOT NULL DEFAULT 0,
                    "ContentRevision" integer NOT NULL DEFAULT 1,
                    "ModerationState" integer NOT NULL DEFAULT 0,
                    "ApprovedRevision" integer,
                    "ModerationSubmissionId" uuid,
                    "ModerationDecisionSequence" bigint NOT NULL,
                    "RejectionReason" text,
                    "DeletedAtUtc" timestamp with time zone,
                    "HelpfulCount" integer NOT NULL,
                    "FirstPublishedAtUtc" timestamp with time zone,
                    "RowVersion" uuid NOT NULL,
                    "Rating_Place" integer NOT NULL,
                    "Rating_Service" integer NOT NULL,
                    "Rating_Coffee" integer NOT NULL,
                    "Rating_AverageRating" numeric NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "UpdatedAtUtc" timestamp with time zone,
                    CONSTRAINT "PK_CanonicalCheckIns" PRIMARY KEY ("Id"),
                    CONSTRAINT "CK_CanonicalCheckIns_CompleteContent" CHECK (("Note" IS NOT NULL AND length(btrim("Note")) BETWEEN 1 AND 1000 AND "Rating_Place" BETWEEN 1 AND 5 AND "Rating_Service" BETWEEN 1 AND 5 AND "Rating_Coffee" BETWEEN 1 AND 5)),
                    CONSTRAINT "CK_CanonicalCheckIns_Visibility" CHECK ("Visibility" IN (0, 1)),
                    CONSTRAINT "FK_CanonicalCheckIns_Shops_ShopId" FOREIGN KEY ("ShopId") REFERENCES "Shops" ("Id") ON DELETE CASCADE
                );

                CREATE TABLE "CheckInHelpfulVotes" (
                    "CheckInId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_CheckInHelpfulVotes" PRIMARY KEY ("CheckInId", "UserId"),
                    CONSTRAINT "FK_CheckInHelpfulVotes_CanonicalCheckIns_CheckInId" FOREIGN KEY ("CheckInId") REFERENCES "CanonicalCheckIns" ("Id") ON DELETE RESTRICT
                );

                CREATE INDEX "IX_CanonicalCheckIns_ShopId" ON "CanonicalCheckIns" ("ShopId");

                CREATE INDEX "IX_CanonicalCheckIns_UserId" ON "CanonicalCheckIns" ("UserId");

                CREATE INDEX "IX_CanonicalCheckIns_UserId_ShopId" ON "CanonicalCheckIns" ("UserId", "ShopId");

                CREATE INDEX "IX_CanonicalCheckIns_UserId_ShopId_VisitedAt_CreatedAtUtc_Id" ON "CanonicalCheckIns" ("UserId", "ShopId", "VisitedAt", "CreatedAtUtc", "Id");

                CREATE INDEX "IX_CanonicalCheckIns_Visibility_ModerationState_CreatedAtUtc_Id" ON "CanonicalCheckIns" ("Visibility", "ModerationState", "CreatedAtUtc", "Id");

                CREATE INDEX "IX_CheckInHelpfulVotes_UserId_CheckInId" ON "CheckInHelpfulVotes" ("UserId", "CheckInId");
                """);
            migrationBuilder.CreateIndex(name: "IX_ShopPhotos_CanonicalCheckInId", table: "ShopPhotos", column: "CanonicalCheckInId");
            migrationBuilder.AddForeignKey(name: "FK_ShopPhotos_CanonicalCheckIns_CanonicalCheckInId", table: "ShopPhotos",
                column: "CanonicalCheckInId", principalTable: "CanonicalCheckIns", principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Preserve canonical and archived data; restore coordinated backups for an application rollback.");
        }
    }
}
