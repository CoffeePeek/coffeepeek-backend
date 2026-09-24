namespace CoffeePeek.Contract.Dtos;

/// <param name="FullUrl">
/// Public URL, filled only on read responses (review / check-in photos). Additive and optional:
/// ignored on input and absent from older messages.
/// </param>
public record UploadedPhotoDto(
    string FileName,
    string ContentType,
    string StorageKey,
    long Size,
    string? FullUrl = null
);
