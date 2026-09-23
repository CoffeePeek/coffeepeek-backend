using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DetachReviewPhotosFromShops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Review photos were created with the shop FK too, so they appeared in (and cascaded with) the shop gallery.
            migrationBuilder.Sql(
                "UPDATE \"ShopPhotos\" SET \"ModerationShopId\" = NULL WHERE \"ModerationReviewId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data fix only; the original (incorrect) shop links are not restored.
        }
    }
}
