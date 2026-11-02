using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavoriteCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteCollections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FavoriteCollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteCollectionItems", x => new { x.CollectionId, x.Position });
                    table.CheckConstraint("CK_FavoriteCollectionItems_Kind", "\"Kind\" IN ('coffee_shop', 'roaster')");
                    table.ForeignKey(
                        name: "FK_FavoriteCollectionItems_FavoriteCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "FavoriteCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteCollectionItems_CollectionId_Kind_EntityId",
                table: "FavoriteCollectionItems",
                columns: new[] { "CollectionId", "Kind", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteCollections_OwnerId_CreatedAtUtc",
                table: "FavoriteCollections",
                columns: new[] { "OwnerId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavoriteCollectionItems");

            migrationBuilder.DropTable(
                name: "FavoriteCollections");
        }
    }
}
