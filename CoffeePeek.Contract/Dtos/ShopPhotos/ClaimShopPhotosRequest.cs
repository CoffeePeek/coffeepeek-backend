namespace CoffeePeek.Contract.Dtos.ShopPhotos;

public record ClaimShopPhotosRequest(
    Guid ShopId,
    ShopPhotoAttachmentKind Kind,
    IReadOnlyList<string> StorageKeys);
