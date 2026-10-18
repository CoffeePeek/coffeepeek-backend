using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAuthorBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicAuthorId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerId, x.BlockedId });
                    table.CheckConstraint("CK_UserBlocks_NoSelfBlock", "\"BlockerId\" <> \"BlockedId\"");
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockedId",
                        column: x => x.BlockedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockerId",
                        column: x => x.BlockerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_PublicAuthorId",
                table: "Users",
                column: "PublicAuthorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedId_BlockerId",
                table: "UserBlocks",
                columns: new[] { "BlockedId", "BlockerId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockerId_CreatedAtUtc_BlockedId",
                table: "UserBlocks",
                columns: new[] { "BlockerId", "CreatedAtUtc", "BlockedId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropIndex(
                name: "IX_Users_PublicAuthorId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PublicAuthorId",
                table: "Users");
        }
    }
}
