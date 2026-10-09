namespace CoffeePeek.Contract.Events.Menu;

// Keep the existing type name and positional constructor for queued legacy Wolverine messages.
public record ParseMenuRequestedEvent(
    MenuParseSourceKind SourceKind,
    Guid SourceId,
    Guid? PublishedShopId,
    IReadOnlyList<MenuPhotoRef> Photos,
    Guid? RequestedByUserId)
{
    public Guid? JobId { get; init; }
    public long? MenuRevision { get; init; }
    public string? PhotoSetHash { get; init; }
}
