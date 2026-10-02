using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class AddConsumedDrinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "Reviews", "CheckIns" })
            foreach (var column in new[] { "DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn" })
            {
                var length = column == "DrinkSlug" ? 50 : 100;
                migrationBuilder.AddColumn<string>(column, table, type: $"character varying({length})", maxLength: length, nullable: true);
            }
        migrationBuilder.InsertData(
            table: "CoffeeDrinkDefinitions",
            columns: new[] { "Id", "Slug", "NameRu", "NameEn", "Category", "Kind", "Aliases", "SortOrder", "IsActive", "CreatedAtUtc" },
            values: new object[] { System.Guid.Parse("b1000000-0000-4000-8000-000000000015"), "lungo", "Лунго", "Lungo", 1, 1,
                "лунго,lungo", 25, true, new System.DateTime(2026, 10, 2, 0, 0, 0, System.DateTimeKind.Utc) });
        migrationBuilder.Sql("""
            UPDATE "CoffeeDrinkDefinitions"
            SET "Aliases" = replace("Aliases", ',lungo', '')
            WHERE "Slug" = 'americano';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the catalog row: shop menus may reference this drink after deployment.
        foreach (var table in new[] { "Reviews", "CheckIns" })
            foreach (var column in new[] { "DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn" })
                migrationBuilder.DropColumn(column, table);
    }
}
