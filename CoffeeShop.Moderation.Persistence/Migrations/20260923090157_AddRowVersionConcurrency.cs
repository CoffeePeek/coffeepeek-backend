using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <summary>
    /// Maps the "Version" row-version properties to PostgreSQL's system column xmin.
    /// xmin already exists on every table, so the scaffolded AddColumn/DropColumn operations are removed:
    /// only the model snapshot changes.
    /// </summary>
    public partial class AddRowVersionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
