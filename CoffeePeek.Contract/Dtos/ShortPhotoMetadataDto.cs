namespace CoffeePeek.Contract.Dtos;

public class ShortPhotoMetadataDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; }
    public string StorageKey { get; init; }
    public string FullUrl { get; init; }
    public PhotoUrlsDto? Urls { get; init; }
    public int SortIndex { get; init; }
    public bool IsPrimary { get; init; }
}
