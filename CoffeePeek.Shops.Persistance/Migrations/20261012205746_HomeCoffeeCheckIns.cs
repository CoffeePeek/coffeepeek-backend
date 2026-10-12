using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class HomeCoffeeCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ShopId",
                table: "CanonicalCheckIns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "BeanNameSnapshot",
                table: "CanonicalCheckIns",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrewDurationSeconds",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrewMethodCode",
                table: "CanonicalCheckIns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CoffeeProductId",
                table: "CanonicalCheckIns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseGrams",
                table: "CanonicalCheckIns",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PreparedOn",
                table: "CanonicalCheckIns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TasteScore",
                table: "CanonicalCheckIns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCheckIns_CoffeeProductId",
                table: "CanonicalCheckIns",
                column: "CoffeeProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCheckIns_UserId_Kind_CoffeeProductId",
                table: "CanonicalCheckIns",
                columns: new[] { "UserId", "Kind", "CoffeeProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCheckIns_UserId_Kind_PreparedOn_VisitedAt_Id",
                table: "CanonicalCheckIns",
                columns: new[] { "UserId", "Kind", "PreparedOn", "VisitedAt", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_HomeDetails",
                table: "CanonicalCheckIns",
                sql: "(\"BrewMethodCode\" IS NULL OR \"BrewMethodCode\" IN ('espresso', 'pour_over', 'batch_drip', 'moka_pot', 'cezve', 'aeropress', 'french_press', 'other')) AND (\"CoffeeProductId\" IS NULL OR \"BeanNameSnapshot\" IS NOT NULL AND length(btrim(\"BeanNameSnapshot\")) > 0) AND (\"DoseGrams\" IS NULL OR \"DoseGrams\" > 0 AND \"DoseGrams\" <= 10000) AND (\"BrewDurationSeconds\" IS NULL OR \"BrewDurationSeconds\" BETWEEN 1 AND 604800) AND (\"TasteScore\" IS NULL OR \"TasteScore\" BETWEEN 1 AND 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CanonicalCheckIns_KindShape",
                table: "CanonicalCheckIns",
                sql: "(\"Kind\" = 0 AND \"ShopId\" IS NOT NULL AND \"PreparedOn\" IS NULL AND \"BrewMethodCode\" IS NULL AND \"CoffeeProductId\" IS NULL AND \"BeanNameSnapshot\" IS NULL AND \"DoseGrams\" IS NULL AND \"BrewDurationSeconds\" IS NULL AND \"TasteScore\" IS NULL) OR (\"Kind\" = 1 AND \"ShopId\" IS NULL AND \"PreparedOn\" IS NOT NULL AND \"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL AND \"Visibility\" = 0 AND \"ModerationState\" = 0 AND \"ApprovedRevision\" IS NULL AND \"ModerationSubmissionId\" IS NULL AND \"FirstPublishedAtUtc\" IS NULL AND \"HelpfulCount\" = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_CanonicalCheckIns_CoffeeProducts_CoffeeProductId",
                table: "CanonicalCheckIns",
                column: "CoffeeProductId",
                principalTable: "CoffeeProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Home check-ins may contain new private diary and photo data. Use a reconciled forward migration instead of dropping them.");
        }
    }
}
