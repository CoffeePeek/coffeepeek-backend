namespace CoffeePeek.Contract.Dtos;

/// <summary>
/// Trusted metadata for a photo the caller uploaded through MediaService.
/// FileName/ContentType come from the presign-time metadata, SizeBytes from the stored object.
/// </summary>
public record ResolvedUploadedPhotoDto(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes
);
