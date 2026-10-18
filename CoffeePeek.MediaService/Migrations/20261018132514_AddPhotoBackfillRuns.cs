using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoBackfillRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BackfillRunId",
                table: "PhotoVariantJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PhotoVariantJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PhotoBackfillRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecentSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CursorSortAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CursorCheckInId = table.Column<Guid>(type: "uuid", nullable: true),
                    CursorPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    MinBatchIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    ScanComplete = table.Column<bool>(type: "boolean", nullable: false),
                    NextBatchAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScannedCount = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedCount = table.Column<long>(type: "bigint", nullable: false),
                    ExistingCount = table.Column<long>(type: "bigint", nullable: false),
                    BlockedCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoBackfillRuns", x => x.Id);
                    table.CheckConstraint("CK_PhotoBackfillRuns_Bounds", "\"BatchSize\" BETWEEN 1 AND 100 AND \"MinBatchIntervalSeconds\" BETWEEN 5 AND 3600 AND \"RecentSinceUtc\" < \"BeforeUtc\"");
                    table.CheckConstraint("CK_PhotoBackfillRuns_Cursor", "(\"CursorSortAtUtc\" IS NULL AND \"CursorCheckInId\" IS NULL AND \"CursorPhotoId\" IS NULL) OR (\"CursorSortAtUtc\" IS NOT NULL AND \"CursorCheckInId\" IS NOT NULL AND \"CursorPhotoId\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_BackfillRunId",
                table: "PhotoVariantJobs",
                column: "BackfillRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_Priority_Status_NextAttemptAtUtc",
                table: "PhotoVariantJobs",
                columns: new[] { "Priority", "Status", "NextAttemptAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PhotoVariantJobs_Priority",
                table: "PhotoVariantJobs",
                sql: "(\"Priority\" = 0 AND \"BackfillRunId\" IS NULL) OR (\"Priority\" = 1 AND \"BackfillRunId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBackfillRuns_Paused",
                table: "PhotoBackfillRuns",
                column: "Paused",
                unique: true,
                filter: "\"Paused\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "FK_PhotoVariantJobs_PhotoBackfillRuns_BackfillRunId",
                table: "PhotoVariantJobs",
                column: "BackfillRunId",
                principalTable: "PhotoBackfillRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Photo archive checkpoints and queue priorities require a forward migration; disable generation instead.");
        }
    }
}
