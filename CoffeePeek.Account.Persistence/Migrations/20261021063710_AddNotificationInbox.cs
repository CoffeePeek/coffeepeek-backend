using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushNotifications_UserId",
                table: "PushNotifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "PushNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushNotifications_UserId_CreatedAtUtc_Id",
                table: "PushNotifications",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotifications_UserId_ReadAtUtc",
                table: "PushNotifications",
                columns: new[] { "UserId", "ReadAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushNotifications_UserId_CreatedAtUtc_Id",
                table: "PushNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PushNotifications_UserId_ReadAtUtc",
                table: "PushNotifications");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "PushNotifications");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                table: "NotificationPreferences");

            migrationBuilder.CreateIndex(
                name: "IX_PushNotifications_UserId",
                table: "PushNotifications",
                column: "UserId");
        }
    }
}
