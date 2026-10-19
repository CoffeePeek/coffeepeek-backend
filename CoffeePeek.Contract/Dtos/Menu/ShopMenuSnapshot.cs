using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ShopMenuSnapshot(
    DateTime? CapturedAtUtc,
    string Currency,
    PriceRange? SuggestedPriceRange,
    IReadOnlyList<ShopMenuItemSnapshot> Items,
    IReadOnlyList<ShopMenuPhotoSnapshot> Photos,
    string? UnmatchedJson = null);
