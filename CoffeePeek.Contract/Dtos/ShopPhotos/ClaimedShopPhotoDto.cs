namespace CoffeePeek.Contract.Dtos.ShopPhotos;

public record ClaimedShopPhotoDto(
    Guid PhotoId,
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes);
