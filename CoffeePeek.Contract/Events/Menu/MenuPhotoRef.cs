namespace CoffeePeek.Contract.Events.Menu;

public record MenuPhotoRef(
    string FileName,
    string ContentType,
    string StorageKey,
    long SizeBytes,
    Guid? MediaPhotoId = null);
