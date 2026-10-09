using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.MediaService.Migrations;

[DbContext(typeof(MediaDbContext))]
[Migration("20261008211500_ReserveShopPhotoAttachments")]
public sealed class ReserveShopPhotoAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("ShopAttachmentId", "Photos", "uuid", nullable: true);
        migrationBuilder.AddColumn<string>("ShopAttachmentKind", "Photos", "character varying(20)",
            maxLength: 20, nullable: true);
        migrationBuilder.CreateIndex("IX_Photos_ShopAttachmentId_ShopAttachmentKind", "Photos",
            new[] { "ShopAttachmentId", "ShopAttachmentKind" });
        migrationBuilder.Sql("""
            ALTER TABLE "Photos" ADD CONSTRAINT "CK_Photos_ShopAttachmentPair"
            CHECK (("ShopAttachmentId" IS NULL) = ("ShopAttachmentKind" IS NULL));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Photos\" DROP CONSTRAINT \"CK_Photos_ShopAttachmentPair\";");
        migrationBuilder.DropIndex("IX_Photos_ShopAttachmentId_ShopAttachmentKind", "Photos");
        migrationBuilder.DropColumn("ShopAttachmentKind", "Photos");
        migrationBuilder.DropColumn("ShopAttachmentId", "Photos");
    }
}
