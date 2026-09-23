using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoffeeShop.Moderation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Header = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    RatingCoffee = table.Column<int>(type: "integer", nullable: false),
                    RatingPlace = table.Column<int>(type: "integer", nullable: false),
                    RatingService = table.Column<int>(type: "integer", nullable: false),
                    RejectedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ModeratedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModerationStatus = table.Column<int>(type: "integer", nullable: false),
                    IsSoftDelete = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModerationShops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PriceRange = table.Column<int>(type: "integer", nullable: false),
                    ModerationStatus = table.Column<int>(type: "integer", nullable: false),
                    RejectedReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Contact_PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Contact_InstagramLink = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Contact_Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Contact_SiteLink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Location_Address = table.Column<string>(type: "text", nullable: false),
                    Location_IsAddressValidated = table.Column<bool>(type: "boolean", nullable: false),
                    Location_Latitude = table.Column<decimal>(type: "numeric", nullable: true),
                    Location_Longitude = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationShops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Processed = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModerationCoffeeBeanShops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoffeeBeanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationCoffeeBeanShops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationCoffeeBeanShops_ModerationShops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationRoasterShops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationRoasterShops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationRoasterShops_ModerationShops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationShopBrewMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrewMethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationShopBrewMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationShopBrewMethods_ModerationShops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationShopEquipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationShopEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationShopEquipments_ModerationShops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationShopSchedule",
                columns: table => new
                {
                    ModerationShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationShopSchedule", x => new { x.ModerationShopId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModerationShopSchedule_ModerationShops_ModerationShopId",
                        column: x => x.ModerationShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModerationShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopPhotos_ModerationShops_ModerationShopId",
                        column: x => x.ModerationShopId,
                        principalTable: "ModerationShops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationShopScheduleInterval",
                columns: table => new
                {
                    ModerationShopScheduleModerationShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModerationShopScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OpenTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    CloseTime = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationShopScheduleInterval", x => new { x.ModerationShopScheduleModerationShopId, x.ModerationShopScheduleId, x.Id });
                    table.ForeignKey(
                        name: "FK_ModerationShopScheduleInterval_ModerationShopSchedule_Moder~",
                        columns: x => new { x.ModerationShopScheduleModerationShopId, x.ModerationShopScheduleId },
                        principalTable: "ModerationShopSchedule",
                        principalColumns: new[] { "ModerationShopId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCoffeeBeanShops_CoffeeBeanId",
                table: "ModerationCoffeeBeanShops",
                column: "CoffeeBeanId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCoffeeBeanShops_ShopId",
                table: "ModerationCoffeeBeanShops",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ModeratedBy",
                table: "ModerationReviews",
                column: "ModeratedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ModerationStatus",
                table: "ModerationReviews",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_ShopId",
                table: "ModerationReviews",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReviews_UserId",
                table: "ModerationReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasterShops_RoasterId",
                table: "ModerationRoasterShops",
                column: "RoasterId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationRoasterShops_ShopId",
                table: "ModerationRoasterShops",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShopBrewMethods_BrewMethodId",
                table: "ModerationShopBrewMethods",
                column: "BrewMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShopBrewMethods_ShopId",
                table: "ModerationShopBrewMethods",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShopEquipments_EquipmentId",
                table: "ModerationShopEquipments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShopEquipments_ShopId",
                table: "ModerationShopEquipments",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_CityId",
                table: "ModerationShops",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_Location_Latitude_Location_Longitude",
                table: "ModerationShops",
                columns: new[] { "Location_Latitude", "Location_Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_ModerationStatus",
                table: "ModerationShops",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationShops_UserId",
                table: "ModerationShops",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopPhotos_ModerationShopId",
                table: "ShopPhotos",
                column: "ModerationShopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationCoffeeBeanShops");

            migrationBuilder.DropTable(
                name: "ModerationReviews");

            migrationBuilder.DropTable(
                name: "ModerationRoasterShops");

            migrationBuilder.DropTable(
                name: "ModerationShopBrewMethods");

            migrationBuilder.DropTable(
                name: "ModerationShopEquipments");

            migrationBuilder.DropTable(
                name: "ModerationShopScheduleInterval");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "ShopPhotos");

            migrationBuilder.DropTable(
                name: "ModerationShopSchedule");

            migrationBuilder.DropTable(
                name: "ModerationShops");
        }
    }
}
