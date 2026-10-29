using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessOwnershipClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Roasters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfileRevision",
                table: "Roasters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "BusinessOwnershipChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<int>(type: "integer", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessOwnershipChanges", x => x.Id);
                    table.CheckConstraint("CK_BusinessOwnershipChanges_Owners", "\"PreviousOwnerUserId\" IS DISTINCT FROM \"NewOwnerUserId\"");
                    table.CheckConstraint("CK_BusinessOwnershipChanges_Type", "\"BusinessType\" IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "BusinessOwnershipClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<int>(type: "integer", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNameSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BusinessSlugSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApplicantUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Contact = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessOwnershipClaims", x => x.Id);
                    table.CheckConstraint("CK_BusinessOwnershipClaims_Content", "length(btrim(\"Contact\")) BETWEEN 1 AND 320 AND length(btrim(\"Evidence\")) BETWEEN 1 AND 4000");
                    table.CheckConstraint("CK_BusinessOwnershipClaims_Decision", "(\"Status\" = 0 AND \"ReviewedByUserId\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"DecisionReason\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND (\"Status\" = 1 OR (\"DecisionReason\" IS NOT NULL AND length(btrim(\"DecisionReason\")) > 0)))");
                    table.CheckConstraint("CK_BusinessOwnershipClaims_Type", "\"BusinessType\" IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Roasters_OwnerUserId",
                table: "Roasters",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnershipChanges_BusinessType_BusinessId_CreatedAtU~",
                table: "BusinessOwnershipChanges",
                columns: new[] { "BusinessType", "BusinessId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnershipChanges_ClaimId",
                table: "BusinessOwnershipChanges",
                column: "ClaimId",
                unique: true,
                filter: "\"ClaimId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnershipClaims_ApplicantUserId_CreatedAtUtc_Id",
                table: "BusinessOwnershipClaims",
                columns: new[] { "ApplicantUserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnershipClaims_PendingApplicant",
                table: "BusinessOwnershipClaims",
                columns: new[] { "BusinessType", "BusinessId", "ApplicantUserId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnershipClaims_Status_CreatedAtUtc_Id",
                table: "BusinessOwnershipClaims",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BusinessOwnershipClaims")
                     OR EXISTS (SELECT 1 FROM "BusinessOwnershipChanges")
                     OR EXISTS (SELECT 1 FROM "Roasters" WHERE "OwnerUserId" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Ownership history exists; retain this schema and roll forward instead';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "BusinessOwnershipChanges");

            migrationBuilder.DropTable(
                name: "BusinessOwnershipClaims");

            migrationBuilder.DropIndex(
                name: "IX_Roasters_OwnerUserId",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Roasters");

            migrationBuilder.DropColumn(
                name: "ProfileRevision",
                table: "Roasters");
        }
    }
}
