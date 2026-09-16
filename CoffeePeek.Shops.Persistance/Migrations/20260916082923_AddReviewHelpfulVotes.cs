using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewHelpfulVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HelpfulCount",
                table: "Reviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReviewHelpfulVotes",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewHelpfulVotes", x => new { x.ReviewId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ReviewHelpfulVotes_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reviews_HelpfulCount_NonNegative",
                table: "Reviews",
                sql: "\"HelpfulCount\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHelpfulVotes_UserId_ReviewId",
                table: "ReviewHelpfulVotes",
                columns: new[] { "UserId", "ReviewId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewHelpfulVotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reviews_HelpfulCount_NonNegative",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "HelpfulCount",
                table: "Reviews");
        }
    }
}
