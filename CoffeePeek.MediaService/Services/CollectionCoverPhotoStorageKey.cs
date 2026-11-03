namespace CoffeePeek.MediaService.Services;

internal static class CollectionCoverPhotoStorageKey
{
    public static string Sealed(Guid photoId, string uploadKey) =>
        $"collection-covers/sealed/{photoId:N}{Path.GetExtension(uploadKey)}";
}
