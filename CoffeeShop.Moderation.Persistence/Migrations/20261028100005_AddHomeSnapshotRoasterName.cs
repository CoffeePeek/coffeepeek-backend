using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeSnapshotRoasterName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Home_RoasterName",
                table: "ModerationCheckIns",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Home_RoasterName",
                table: "ModerationCheckIns");
        }
    }
}
