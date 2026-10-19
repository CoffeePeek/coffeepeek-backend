using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ShopMenuPhotoSnapshot(
    string FileName,
    string ContentType,
    string StorageKey,
    long SizeBytes,
    Guid? MediaPhotoId = null);
