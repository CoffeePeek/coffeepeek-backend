using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeCatalogAndRoasterTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoffeeImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SnapshotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Products = table.Column<int>(type: "integer", nullable: false),
                    Variants = table.Column<int>(type: "integer", nullable: false),
                    Added = table.Column<int>(type: "integer", nullable: false),
                    MissingAvailabilityApplied = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "C"),
                    SlugRevision = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    ProductKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProductForm = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CompositionKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Processing = table.Column<string>(type: "text", nullable: true),
                    RoastLevel = table.Column<string>(type: "text", nullable: true),
                    Acidity = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    QGraderScoreRaw = table.Column<string>(type: "text", nullable: true),
                    TasteDescriptors = table.Column<string[]>(type: "text[]", nullable: false),
                    BrewRecommendations = table.Column<string[]>(type: "text[]", nullable: false),
                    GrindOptions = table.Column<string[]>(type: "text[]", nullable: false),
                    Features = table.Column<string[]>(type: "text[]", nullable: false),
                    ProtectedFields = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeProducts", x => x.Id);
                    table.CheckConstraint("CK_CoffeeProducts_PublicSlug", "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)");
                    table.ForeignKey(
                        name: "FK_CoffeeProducts_Roasters_RoasterId",
                        column: x => x.RoasterId,
                        principalTable: "Roasters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    NameRu = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RoasterTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoasterTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeProductSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoffeeProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ExternalProductId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SellerName = table.Column<string>(type: "text", nullable: true),
                    SourceBrand = table.Column<string>(type: "text", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewWarnings = table.Column<string[]>(type: "text[]", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPresent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeProductSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeProductSources_CoffeeProducts_CoffeeProductId",
                        column: x => x.CoffeeProductId,
                        principalTable: "CoffeeProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeProductCountries",
                columns: table => new
                {
                    CoffeeProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeProductCountries", x => new { x.CoffeeProductId, x.CountryCode });
                    table.ForeignKey(
                        name: "FK_CoffeeProductCountries_CoffeeProducts_CoffeeProductId",
                        column: x => x.CoffeeProductId,
                        principalTable: "CoffeeProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoffeeProductCountries_Countries_CountryCode",
                        column: x => x.CountryCode,
                        principalTable: "Countries",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoasterTagAssignments",
                columns: table => new
                {
                    RoasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoasterTagAssignments", x => new { x.RoasterId, x.TagId });
                    table.ForeignKey(
                        name: "FK_RoasterTagAssignments_RoasterTags_TagId",
                        column: x => x.TagId,
                        principalTable: "RoasterTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoasterTagAssignments_Roasters_RoasterId",
                        column: x => x.RoasterId,
                        principalTable: "Roasters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeProductPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoffeeProductSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MediaPhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeProductPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeProductPhotos_CoffeeProductSources_CoffeeProductSourc~",
                        column: x => x.CoffeeProductSourceId,
                        principalTable: "CoffeeProductSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoffeeProductSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ExternalVariantId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    WeightGrams = table.Column<int>(type: "integer", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Grind = table.Column<string>(type: "text", nullable: true),
                    RoastPurpose = table.Column<string>(type: "text", nullable: true),
                    SourceAttributesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Availability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AvailabilityScope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeVariants", x => x.Id);
                    table.CheckConstraint("CK_CoffeeVariants_Price", "\"Price\" > 0");
                    table.CheckConstraint("CK_CoffeeVariants_Weight", "\"WeightGrams\" IS NULL OR \"WeightGrams\" > 0");
                    table.ForeignKey(
                        name: "FK_CoffeeVariants_CoffeeProductSources_CoffeeProductSourceId",
                        column: x => x.CoffeeProductSourceId,
                        principalTable: "CoffeeProductSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Code", "IsActive", "NameEn", "NameRu" },
                values: new object[,]
                {
                    { "BI", true, "Burundi", "Бурунди" },
                    { "BO", true, "Bolivia", "Боливия" },
                    { "BR", true, "Brazil", "Бразилия" },
                    { "CG", true, "Congo", "Конго" },
                    { "CM", true, "Cameroon", "Камерун" },
                    { "CN", true, "China", "Китай" },
                    { "CO", true, "Colombia", "Колумбия" },
                    { "CR", true, "Costa Rica", "Коста-Рика" },
                    { "ET", true, "Ethiopia", "Эфиопия" },
                    { "GT", true, "Guatemala", "Гватемала" },
                    { "HN", true, "Honduras", "Гондурас" },
                    { "ID", true, "Indonesia", "Индонезия" },
                    { "IN", true, "India", "Индия" },
                    { "KE", true, "Kenya", "Кения" },
                    { "LA", true, "Laos", "Лаос" },
                    { "MW", true, "Malawi", "Малави" },
                    { "MX", true, "Mexico", "Мексика" },
                    { "NI", true, "Nicaragua", "Никарагуа" },
                    { "PA", true, "Panama", "Панама" },
                    { "PE", true, "Peru", "Перу" },
                    { "PG", true, "Papua New Guinea", "Папуа-Новая Гвинея" },
                    { "RW", true, "Rwanda", "Руанда" },
                    { "SV", true, "El Salvador", "Сальвадор" },
                    { "TZ", true, "Tanzania", "Танзания" },
                    { "UG", true, "Uganda", "Уганда" },
                    { "VE", true, "Venezuela", "Венесуэла" },
                    { "VN", true, "Vietnam", "Вьетнам" },
                    { "YE", true, "Yemen", "Йемен" },
                    { "ZM", true, "Zambia", "Замбия" },
                    { "ZW", true, "Zimbabwe", "Зимбабве" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeImportRuns_SourceKey_CollectedAtUtc",
                table: "CoffeeImportRuns",
                columns: new[] { "SourceKey", "CollectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeImportRuns_SourceKey_SnapshotId",
                table: "CoffeeImportRuns",
                columns: new[] { "SourceKey", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProductCountries_CountryCode",
                table: "CoffeeProductCountries",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProductPhotos_CoffeeProductSourceId_StorageKey",
                table: "CoffeeProductPhotos",
                columns: new[] { "CoffeeProductSourceId", "StorageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProducts_RoasterId_Status",
                table: "CoffeeProducts",
                columns: new[] { "RoasterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProducts_Slug",
                table: "CoffeeProducts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProductSources_CoffeeProductId",
                table: "CoffeeProductSources",
                column: "CoffeeProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeProductSources_SourceKey_ExternalProductId",
                table: "CoffeeProductSources",
                columns: new[] { "SourceKey", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeVariants_CoffeeProductSourceId",
                table: "CoffeeVariants",
                column: "CoffeeProductSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeVariants_SourceKey_ExternalVariantId",
                table: "CoffeeVariants",
                columns: new[] { "SourceKey", "ExternalVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoasterTagAssignments_TagId",
                table: "RoasterTagAssignments",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_RoasterTags_Slug",
                table: "RoasterTags",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoffeeImportRuns");

            migrationBuilder.DropTable(
                name: "CoffeeProductCountries");

            migrationBuilder.DropTable(
                name: "CoffeeProductPhotos");

            migrationBuilder.DropTable(
                name: "CoffeeVariants");

            migrationBuilder.DropTable(
                name: "RoasterTagAssignments");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "CoffeeProductSources");

            migrationBuilder.DropTable(
                name: "RoasterTags");

            migrationBuilder.DropTable(
                name: "CoffeeProducts");
        }
    }
}
