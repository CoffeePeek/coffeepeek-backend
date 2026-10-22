using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime[]>(
                name: "UsernameChangeHistoryUtc",
                table: "Users",
                type: "timestamp with time zone[]",
                nullable: false,
                defaultValueSql: "'{}'::timestamp with time zone[]");

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "UsernameChangeHistoryUtc" = ARRAY["UsernameChangedAtUtc"]
                WHERE "UsernameChangedAtUtc" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsernameChangeHistoryUtc",
                table: "Users");
        }
    }
}
