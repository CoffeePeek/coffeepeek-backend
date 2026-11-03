using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedCollectionLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "private"),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverStorageKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CoverContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCollections", x => x.Id);
                    table.CheckConstraint("CK_SavedCollections_Visibility", "\"Visibility\" IN ('public', 'private')");
                });

            migrationBuilder.CreateTable(
                name: "SavedCollectionCoverUses",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCollectionCoverUses", x => x.PhotoId);
                    table.ForeignKey(
                        name: "FK_SavedCollectionCoverUses_SavedCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "SavedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedCollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCollectionItems", x => new { x.CollectionId, x.Kind, x.EntityId });
                    table.CheckConstraint("CK_SavedCollectionItems_Kind", "\"Kind\" IN ('coffee_shop', 'roaster')");
                    table.ForeignKey(
                        name: "FK_SavedCollectionItems_SavedCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "SavedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedCollectionPreferences",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCollectionPreferences", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_SavedCollectionPreferences_SavedCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "SavedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedCollectionCoverUses_CollectionId",
                table: "SavedCollectionCoverUses",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCollectionItems_CollectionId_Position",
                table: "SavedCollectionItems",
                columns: new[] { "CollectionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedCollectionPreferences_CollectionId",
                table: "SavedCollectionPreferences",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCollections_OwnerId_CreatedAtUtc",
                table: "SavedCollections",
                columns: new[] { "OwnerId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "SavedCollections") THEN
                        RAISE EXCEPTION 'Saved collection data exists; preserve the library tables before schema rollback.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "SavedCollectionCoverUses");

            migrationBuilder.DropTable(
                name: "SavedCollectionItems");

            migrationBuilder.DropTable(
                name: "SavedCollectionPreferences");

            migrationBuilder.DropTable(
                name: "SavedCollections");
        }
    }
}
