namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed record MenuOcrPhotoSnapshot(
    string FileName,
    string ContentType,
    string StorageKey,
    long SizeBytes,
    Guid? MediaPhotoId)
{
    public static MenuOcrPhotoSnapshot From(ShopMenuPhoto photo) =>
        new(photo.FileName, photo.ContentType, photo.StorageKey, photo.SizeBytes, photo.MediaPhotoId);
}
