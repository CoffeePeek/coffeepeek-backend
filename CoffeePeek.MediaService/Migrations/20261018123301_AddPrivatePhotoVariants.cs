using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.MediaService.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivatePhotoVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInPhotoTombstones",
                columns: table => new
                {
                    CheckInId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInPhotoTombstones", x => x.CheckInId);
                });

            migrationBuilder.CreateTable(
                name: "PhotoVariantJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckInId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoVariantJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoVariantJobs_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoVariantArtifacts",
                columns: table => new
                {
                    ObjectKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoVariantArtifacts", x => x.ObjectKey);
                    table.ForeignKey(
                        name: "FK_PhotoVariantArtifacts_PhotoVariantJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PhotoVariantJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoVariants", x => x.Id);
                    table.CheckConstraint("CK_PhotoVariants_Dimensions", "\"Width\" > 0 AND \"Height\" > 0 AND \"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_PhotoVariants_PhotoVariantJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PhotoVariantJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantArtifacts_CreatedAtUtc",
                table: "PhotoVariantArtifacts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantArtifacts_JobId",
                table: "PhotoVariantArtifacts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_CheckInId",
                table: "PhotoVariantJobs",
                column: "CheckInId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_PhotoId_SourceVersion_RecipeVersion",
                table: "PhotoVariantJobs",
                columns: new[] { "PhotoId", "SourceVersion", "RecipeVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_Status_LeaseUntilUtc",
                table: "PhotoVariantJobs",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariantJobs_Status_NextAttemptAtUtc",
                table: "PhotoVariantJobs",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariants_JobId",
                table: "PhotoVariants",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariants_ObjectKey",
                table: "PhotoVariants",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVariants_PhotoId_SourceVersion_RecipeVersion_Purpose_F~",
                table: "PhotoVariants",
                columns: new[] { "PhotoId", "SourceVersion", "RecipeVersion", "Purpose", "Format" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckInPhotoTombstones");

            migrationBuilder.DropTable(
                name: "PhotoVariantArtifacts");

            migrationBuilder.DropTable(
                name: "PhotoVariants");

            migrationBuilder.DropTable(
                name: "PhotoVariantJobs");
        }
    }
}
