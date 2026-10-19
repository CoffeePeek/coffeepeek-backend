using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ParsedMenuItemDto(
    string Slug,
    decimal? Price,
    string RawName,
    string? NameRu = null,
    string? NameEn = null,
    CoffeeDrinkCategory? Category = null);
