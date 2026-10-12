namespace CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

public sealed class MenuDraftPhoto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public string StorageKey { get; init; } = "";
    public long SizeBytes { get; init; }
    public Guid? MediaPhotoId { get; init; }
}
