namespace CoffeePeek.Contract.Dtos;

/// <param name="FullUrl">
/// Public URL, filled only on responses (review / check-in / shop change request photos). Additive and optional:
/// ignored on input and absent from older messages.
/// </param>
public record UploadedPhotoDto(
    string FileName,
    string ContentType,
    string StorageKey,
    long Size,
    string? FullUrl = null
);
