using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Account.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireReviewCountOnMain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The ignored legacy column remains archived; new user inserts must satisfy its old NOT NULL constraint.
            migrationBuilder.Sql("ALTER TABLE \"Users\" ALTER COLUMN \"ReviewCount\" SET DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keeping the harmless archive default also permits old binaries to read historical counters.
        }
    }
}
