namespace CoffeePeek.Contract.Events.Menu;

public record ParseMenuRequestedEvent(
    MenuParseSourceKind SourceKind,
    Guid SourceId,
    Guid? PublishedShopId,
    IReadOnlyList<MenuPhotoRef> Photos,
    Guid? RequestedByUserId,
    Guid? ParseRequestId = null);
