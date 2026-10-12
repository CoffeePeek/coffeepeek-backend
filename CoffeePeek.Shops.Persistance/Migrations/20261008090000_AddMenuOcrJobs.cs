using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class AddMenuOcrJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ActiveOcrJobId",
            table: "ShopMenus",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "InputRevision",
            table: "ShopMenus",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "MenuOcrJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceKind = table.Column<int>(type: "integer", nullable: false),
                SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                PublishedShopId = table.Column<Guid>(type: "uuid", nullable: true),
                ShopMenuId = table.Column<Guid>(type: "uuid", nullable: true),
                MenuRevision = table.Column<long>(type: "bigint", nullable: true),
                PhotoSetHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                InputPhotosJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResultStoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MenuOcrJobs", x => x.Id);
                table.CheckConstraint("CK_MenuOcrJobs_Status", "\"Status\" BETWEEN 0 AND 5");
                table.CheckConstraint("CK_MenuOcrJobs_SourceKind", "\"SourceKind\" BETWEEN 1 AND 3");
                table.CheckConstraint("CK_MenuOcrJobs_MenuRevision", "\"MenuRevision\" IS NULL OR \"MenuRevision\" >= 0");
                table.ForeignKey(
                    name: "FK_MenuOcrJobs_ShopMenus_ShopMenuId",
                    column: x => x.ShopMenuId,
                    principalTable: "ShopMenus",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MenuOcrJobs_ShopMenuId_Status",
            table: "MenuOcrJobs",
            columns: new[] { "ShopMenuId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_MenuOcrJobs_SourceKind_SourceId_RequestedAtUtc",
            table: "MenuOcrJobs",
            columns: new[] { "SourceKind", "SourceId", "RequestedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MenuOcrJobs");
        migrationBuilder.DropColumn(name: "ActiveOcrJobId", table: "ShopMenus");
        migrationBuilder.DropColumn(name: "InputRevision", table: "ShopMenus");
    }
}
