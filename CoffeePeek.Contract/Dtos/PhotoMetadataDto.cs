namespace CoffeePeek.Contract.Dtos;

public class PhotoMetadataDto
{
    public Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string StorageKey { get; init; }
    public string? FullUrl { get; init; }
    public PhotoUrlsDto? Urls { get; init; }
    public long SizeBytes { get; init; }
    public Guid OwnerId { get; init; }
    public DateTime UploadedAt { get; init; }
    public int SortIndex { get; init; }
}
