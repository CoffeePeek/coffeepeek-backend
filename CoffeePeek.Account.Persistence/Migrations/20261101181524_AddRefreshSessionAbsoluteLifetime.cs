using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshSessionAbsoluteLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SessionExpiresAtUtc",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            // Rotation previously discarded the original login date. Preserve existing token
            // validity and use its issuance as the baseline; never revive expired/revoked tokens.
            migrationBuilder.Sql("""
                UPDATE "RefreshTokens"
                SET "SessionExpiresAtUtc" = "CreatedAtUtc" + INTERVAL '8760 hours';
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "SessionExpiresAtUtc",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionExpiresAtUtc",
                table: "RefreshTokens");
        }
    }
}
