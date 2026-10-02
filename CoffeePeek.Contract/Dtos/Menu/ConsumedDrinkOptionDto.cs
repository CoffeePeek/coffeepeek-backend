using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ConsumedDrinkOptionDto(string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory? Category, int SortOrder);
