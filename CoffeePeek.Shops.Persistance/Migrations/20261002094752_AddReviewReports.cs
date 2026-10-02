using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemovedByAdmin",
                table: "Reviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Reviews",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ReviewReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewReports", x => x.Id);
                    table.CheckConstraint("CK_ReviewReports_Resolution", "(\"Status\" = 0 AND \"ResolvedByAdminId\" IS NULL AND \"ResolvedAtUtc\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ResolvedByAdminId\" IS NOT NULL AND \"ResolvedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_ReviewReports_Text", "length(btrim(\"Text\")) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_ReviewReports_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_ReportedByUserId",
                table: "ReviewReports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_ReviewId",
                table: "ReviewReports",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_Status_CreatedAtUtc_Id",
                table: "ReviewReports",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewReports");

            migrationBuilder.DropColumn(
                name: "IsRemovedByAdmin",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Reviews");
        }
    }
}
