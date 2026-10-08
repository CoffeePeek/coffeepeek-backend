using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryClassificationsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrewPurposeCode",
                table: "CoffeeVariants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrewPurposeProtected",
                table: "CoffeeVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AcidityCode",
                table: "CoffeeProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaffeineCode",
                table: "CoffeeProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ClassificationProtectedFields",
                table: "CoffeeProducts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "CompositionCode",
                table: "CoffeeProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "DefaultBrewPurposes",
                table: "CoffeeProducts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "FermentationCodes",
                table: "CoffeeProducts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "ProcessingCodes",
                table: "CoffeeProducts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "RoastLevelCode",
                table: "CoffeeProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "TasteGroupCodes",
                table: "CoffeeProducts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "CoffeeFilterValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeFilterValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryFavorites",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryFavorites", x => new { x.UserId, x.Kind, x.EntityId });
                });

            migrationBuilder.InsertData(
                table: "CoffeeFilterValues",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "Description", "GroupCode", "IsActive", "Name", "SortOrder", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("12a2e090-d43a-15bf-131e-61173de6d063"), "anaerobic", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "fermentation", true, "Анаэробная", 10, null },
                    { new Guid("19070462-1575-2b3e-5070-5f35559aff64"), "bright", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "acidity", true, "Яркая", 30, null },
                    { new Guid("216e51b2-c578-02b8-8096-7a1c324bdb17"), "floral", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Цветы", 70, null },
                    { new Guid("254d85ed-42ef-41bd-1081-a7892f8a265e"), "dark", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "roast", true, "Тёмная", 30, null },
                    { new Guid("29da4e1e-0b46-f6e4-f8eb-39de1fee4b43"), "low", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "acidity", true, "Низкая", 10, null },
                    { new Guid("2e0ba3a7-9d39-8d1c-e242-e205a08873ca"), "medium", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "roast", true, "Средняя", 20, null },
                    { new Guid("32113231-a541-15c4-11e2-36f9329a42b9"), "light", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "roast", true, "Светлая", 10, null },
                    { new Guid("342ce57b-c707-e9d5-5ea2-81b00102202f"), "balanced", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "acidity", true, "Умеренная", 20, null },
                    { new Guid("351f800f-93c5-31a2-2cef-1b5532f5d0ee"), "semi-anaerobic", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "fermentation", true, "Полуанаэробная", 20, null },
                    { new Guid("5178c1b8-94dd-dce8-bd90-378136fb4e53"), "regular", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "caffeine", true, "Обычный", 10, null },
                    { new Guid("59675992-d8db-d8eb-af54-30b405abcb07"), "wet-hulled", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "processing", true, "Вет-халл", 40, null },
                    { new Guid("6da24927-4805-5e2b-cb6e-c2c6eb5b2fa6"), "espresso", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "brew", true, "Для эспрессо", 20, null },
                    { new Guid("736fae16-cd48-ab03-10fd-01f9472d3168"), "chocolate", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Шоколад", 10, null },
                    { new Guid("7a3aab3f-a00e-8949-d257-87d12347f5d3"), "citrus", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Цитрусы", 50, null },
                    { new Guid("81086b71-d50d-722b-87a9-2584d92cf93a"), "single-origin", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "composition", true, "Моносорт", 10, null },
                    { new Guid("89331ac7-2e53-d3ce-00a8-283992ce95cc"), "washed", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "processing", true, "Мытая", 10, null },
                    { new Guid("a50087f0-7bdb-c65f-6dca-69ef7370f986"), "caramel", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Карамель", 30, null },
                    { new Guid("a92ddce7-5e29-85a2-db4a-1a2f60784d34"), "filter", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "brew", true, "Для фильтра", 10, null },
                    { new Guid("ac497bdc-51a5-023b-285e-ab0eb8f99758"), "lowcaf", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "caffeine", true, "Лоукаф", 30, null },
                    { new Guid("aec4a4e2-dcbc-508e-e844-b2f0f3150cfb"), "honey", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "processing", true, "Хани", 30, null },
                    { new Guid("ba0b140f-7c84-2836-c7ac-9653ccc59849"), "blend", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "composition", true, "Купаж", 20, null },
                    { new Guid("badc0b70-4a55-c2f5-3946-8aae4aa4cd22"), "decaf", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "caffeine", true, "Декаф", 20, null },
                    { new Guid("c7a3e9ea-a772-d015-f21f-0a954060e591"), "spicy", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Пряности", 80, null },
                    { new Guid("c91f50c1-e453-8174-276d-3d245dcb2559"), "nut", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Орехи", 20, null },
                    { new Guid("ce92527e-f69b-a294-6f72-fabaf422d238"), "tropical", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Тропические фрукты", 60, null },
                    { new Guid("d080c9bd-2948-0679-7ed5-003a64d02b39"), "omni", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "brew", true, "Универсальная обжарка", 30, null },
                    { new Guid("dbdaf974-bdaa-8594-0956-aa177d323f13"), "natural", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "processing", true, "Натуральная", 20, null },
                    { new Guid("ecb03f92-2df1-7150-8d3d-71b8e4ace8ee"), "berry", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "taste", true, "Ягоды", 40, null },
                    { new Guid("fca22118-6efa-c7be-94bc-7a78069250ff"), "experimental", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "fermentation", true, "Экспериментальная", 30, null }
                });

            // Preserve an existing administrator-created tag with the same reserved slug.
            migrationBuilder.Sql("""
                INSERT INTO "RoasterTags" ("Id", "CreatedAtUtc", "Description", "IsActive", "Name", "Slug", "SortOrder", "UpdatedAtUtc")
                VALUES
                    ('19ab1acf-0048-9c36-0be9-09f36cff9972', TIMESTAMPTZ '1970-01-01 00:00:00+00', 'Подтверждён онлайн-заказ непосредственно у обжарщика; ссылка стороннего продавца сама по себе не подтверждает этот тег.', TRUE, 'Онлайн-заказ', 'online-order', 10, NULL),
                    ('69f4da38-1229-6a98-123a-1b1ef5109cd7', TIMESTAMPTZ '1970-01-01 00:00:00+00', 'Подтверждена возможность забрать заказ у обжарщика.', TRUE, 'Самовывоз', 'pickup', 20, NULL),
                    ('f4e52e41-8b12-689f-762b-fe97c5005228', TIMESTAMPTZ '1970-01-01 00:00:00+00', 'Подтверждена действующая подписка на регулярные поставки кофе.', TRUE, 'Подписка на кофе', 'subscription', 30, NULL),
                    ('803fd248-2490-ff13-b849-1aee90ca948e', TIMESTAMPTZ '1970-01-01 00:00:00+00', 'Подтверждена работа с оптовыми заказами.', TRUE, 'Опт', 'wholesale', 40, NULL),
                    ('c847057e-c11a-7503-ce81-556d9e5f1f35', TIMESTAMPTZ '1970-01-01 00:00:00+00', 'Подтверждены доступные обучающие программы.', TRUE, 'Обучение', 'training', 50, NULL)
                ON CONFLICT ("Slug") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeFilterValues_GroupCode_Code",
                table: "CoffeeFilterValues",
                columns: new[] { "GroupCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryFavorites_Kind_EntityId",
                table: "DiscoveryFavorites",
                columns: new[] { "Kind", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoffeeFilterValues");

            migrationBuilder.DropTable(
                name: "DiscoveryFavorites");

            // Assignments belong to existing administration functionality; keep any tag still in use.
            migrationBuilder.Sql("""
                DELETE FROM "RoasterTags" AS t
                WHERE t."Id" IN ('19ab1acf-0048-9c36-0be9-09f36cff9972', '69f4da38-1229-6a98-123a-1b1ef5109cd7', 'f4e52e41-8b12-689f-762b-fe97c5005228', '803fd248-2490-ff13-b849-1aee90ca948e', 'c847057e-c11a-7503-ce81-556d9e5f1f35')
                AND NOT EXISTS (SELECT 1 FROM "RoasterTagAssignments" AS a WHERE a."TagId" = t."Id");
                """);

            migrationBuilder.DropColumn(
                name: "BrewPurposeCode",
                table: "CoffeeVariants");

            migrationBuilder.DropColumn(
                name: "BrewPurposeProtected",
                table: "CoffeeVariants");

            migrationBuilder.DropColumn(
                name: "AcidityCode",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "CaffeineCode",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "ClassificationProtectedFields",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "CompositionCode",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "DefaultBrewPurposes",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "FermentationCodes",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "ProcessingCodes",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "RoastLevelCode",
                table: "CoffeeProducts");

            migrationBuilder.DropColumn(
                name: "TasteGroupCodes",
                table: "CoffeeProducts");
        }
    }
}
