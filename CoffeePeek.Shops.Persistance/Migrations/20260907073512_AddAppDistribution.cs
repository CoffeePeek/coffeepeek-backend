using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShopsDbContext))]
    [Migration("20260907073512_AddAppDistribution")]
    public partial class AddAppDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AndroidReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VersionCode = table.Column<int>(type: "integer", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AndroidReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDistributionAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OldValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDistributionAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDistributionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AndroidGooglePlayUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IosAppStoreUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AndroidGooglePlayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AndroidApkEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IosAppStoreEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDistributionSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDownloadRedirectEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Referer = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDownloadRedirectEvents", x => x.Id);
                });

            migrationBuilder.Sql($"""
                INSERT INTO "AppDistributionSettings"
                    ("Id", "AndroidGooglePlayEnabled", "AndroidApkEnabled", "IosAppStoreEnabled", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ('{AppDistributionSettings.SingletonId}', FALSE, FALSE, FALSE, TIMESTAMPTZ '2026-09-07 00:00:00+00', NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AndroidReleases_IsActive",
                table: "AndroidReleases",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AndroidReleases_VersionCode",
                table: "AndroidReleases",
                column: "VersionCode");

            migrationBuilder.CreateIndex(
                name: "IX_AppDistributionAuditLogs_CreatedAtUtc",
                table: "AppDistributionAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppDistributionAuditLogs_UserId",
                table: "AppDistributionAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDownloadRedirectEvents_Channel",
                table: "AppDownloadRedirectEvents",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_AppDownloadRedirectEvents_Timestamp",
                table: "AppDownloadRedirectEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AndroidReleases");
            migrationBuilder.DropTable(name: "AppDistributionAuditLogs");
            migrationBuilder.DropTable(name: "AppDistributionSettings");
            migrationBuilder.DropTable(name: "AppDownloadRedirectEvents");
        }
    }
}
