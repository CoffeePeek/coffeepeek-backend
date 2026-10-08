using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalCheckInSnapshotsOnMain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old review snapshots and their existing photo associations remain untouched archives.
            migrationBuilder.AddColumn<Guid>(name: "ModerationCheckInId", table: "ShopPhotos", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "CheckInPhotoId", table: "ShopPhotos", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<int>(name: "SortIndex", table: "ShopPhotos", type: "integer", nullable: false, defaultValue: 0);
            // DDL generated from the current Npgsql EF model.
            migrationBuilder.Sql("""
                CREATE TABLE "ModerationCheckIns" (
                    "Id" uuid NOT NULL,
                    "Comment" character varying(1000) NOT NULL,
                    "UserId" uuid NOT NULL,
                    "UserName" character varying(30) NOT NULL,
                    "ShopId" uuid NOT NULL,
                    "CheckInId" uuid NOT NULL,
                    "ContentRevision" integer NOT NULL,
                    "DrinkSlug" character varying(50),
                    "CustomDrinkName" character varying(100),
                    "DrinkNameRu" character varying(100),
                    "DrinkNameEn" character varying(100),
                    "VisitedAtUtc" timestamp with time zone NOT NULL,
                    "DecisionSequence" bigint NOT NULL,
                    "Rating_Place" integer NOT NULL,
                    "Rating_Service" integer NOT NULL,
                    "Rating_Coffee" integer NOT NULL,
                    "Rating_AverageRating" numeric NOT NULL,
                    "RejectedReason" character varying(1000),
                    "ModeratedBy" uuid,
                    "ModeratedAt" timestamp with time zone,
                    "ModerationStatus" integer NOT NULL,
                    "IsSoftDelete" boolean NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "UpdatedAtUtc" timestamp with time zone,
                    CONSTRAINT "PK_ModerationCheckIns" PRIMARY KEY ("Id"),
                    CONSTRAINT "CK_ModerationCheckIns_CanonicalSnapshot" CHECK ("CheckInId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ContentRevision" > 0 AND length(btrim("Comment")) > 0 AND "Rating_Place" BETWEEN 1 AND 5 AND "Rating_Service" BETWEEN 1 AND 5 AND "Rating_Coffee" BETWEEN 1 AND 5)
                );

                CREATE UNIQUE INDEX "IX_ModerationCheckIns_CheckInId_ContentRevision" ON "ModerationCheckIns" ("CheckInId", "ContentRevision");

                CREATE INDEX "IX_ModerationCheckIns_ModeratedBy" ON "ModerationCheckIns" ("ModeratedBy");

                CREATE INDEX "IX_ModerationCheckIns_ModerationStatus_CreatedAtUtc" ON "ModerationCheckIns" ("ModerationStatus", "CreatedAtUtc");

                CREATE INDEX "IX_ModerationCheckIns_ShopId" ON "ModerationCheckIns" ("ShopId");

                CREATE INDEX "IX_ModerationCheckIns_UserId" ON "ModerationCheckIns" ("UserId");
                """);
            migrationBuilder.CreateIndex(name: "IX_ShopPhotos_ModerationCheckInId", table: "ShopPhotos", column: "ModerationCheckInId");
            migrationBuilder.AddForeignKey(name: "FK_ShopPhotos_ModerationCheckIns_ModerationCheckInId", table: "ShopPhotos",
                column: "ModerationCheckInId", principalTable: "ModerationCheckIns", principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Preserve canonical and archived data; restore coordinated backups for an application rollback.");
        }
    }
}
