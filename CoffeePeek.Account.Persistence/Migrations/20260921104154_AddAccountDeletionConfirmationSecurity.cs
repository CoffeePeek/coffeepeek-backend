using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletionConfirmationSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailsSentInWindow",
                table: "AccountDeletionRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResendWindowStartedAtUtc",
                table: "AccountDeletionRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailsSentInWindow",
                table: "AccountDeletionRequests");

            migrationBuilder.DropColumn(
                name: "ResendWindowStartedAtUtc",
                table: "AccountDeletionRequests");
        }
    }
}
