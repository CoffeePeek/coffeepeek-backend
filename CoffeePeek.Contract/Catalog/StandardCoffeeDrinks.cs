using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Catalog;

/// <summary>v1 catalog snapshot for draft expansion (IDs live in Shops seed).</summary>
public static class StandardCoffeeDrinks
{
    public static readonly CoffeeDrinkDefinitionDto[] All =
    [
        new("espresso", "Эспрессо", "Espresso", CoffeeDrinkCategory.Espresso, 10),
        new("lungo", "Лунго", "Lungo", CoffeeDrinkCategory.Espresso, 25),
        new("doppio", "Доппио", "Doppio", CoffeeDrinkCategory.Espresso, 20),
        new("americano", "Американо", "Americano", CoffeeDrinkCategory.Espresso, 30),
        new("cappuccino", "Капучино", "Cappuccino", CoffeeDrinkCategory.Espresso, 40),
        new("latte", "Латте", "Latte", CoffeeDrinkCategory.Espresso, 50),
        new("flat_white", "Флэт уайт", "Flat white", CoffeeDrinkCategory.Espresso, 60),
        new("cortado", "Кортадо", "Cortado", CoffeeDrinkCategory.Espresso, 70),
        new("macchiato", "Макиато", "Macchiato", CoffeeDrinkCategory.Espresso, 80),
        new("raf", "Раф", "Raf", CoffeeDrinkCategory.Espresso, 90),
        new("batch_brew", "Фильтр", "Batch brew", CoffeeDrinkCategory.Filter, 100),
        new("v60", "V60 / воронка", "V60", CoffeeDrinkCategory.Filter, 110),
        new("kalita", "Калита", "Kalita", CoffeeDrinkCategory.Filter, 120),
        new("chemex", "Кемекс", "Chemex", CoffeeDrinkCategory.Filter, 130),
        new("aeropress", "Аэропресс", "AeroPress", CoffeeDrinkCategory.Filter, 140)
    ];
}
