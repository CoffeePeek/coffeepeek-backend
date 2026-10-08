using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ShopMenuDto(
    DateTime? CapturedAtUtc,
    DateTime? UpdatedAtUtc,
    string Currency,
    MenuParseStatus ParseStatus,
    string? ParseError,
    PriceRange? SuggestedPriceRange,
    IReadOnlyList<ShopMenuItemDto> Items,
    IReadOnlyList<ShortPhotoMetadataDto> Photos,
    long? InputRevision = null);
