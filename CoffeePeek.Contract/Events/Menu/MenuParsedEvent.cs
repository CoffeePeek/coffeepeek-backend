using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Events.Menu;

public record MenuParsedEvent(
    MenuParseSourceKind SourceKind,
    Guid SourceId,
    Guid? PublishedShopId,
    bool Success,
    string? Error,
    PriceRange? SuggestedPriceRange,
    IReadOnlyList<ParsedMenuItemDto> Items,
    IReadOnlyList<UnmatchedMenuItemDto> Unmatched,
    DateTime CapturedAtUtc)
{
    public Guid? JobId { get; init; }
}
