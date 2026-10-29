namespace CoffeePeek.MediaService.Services;

/// <summary>The photo-owned immutable destination, shared by sealing and recovery after a failed metadata save.</summary>
internal static class ShelfBagPhotoStorageKey
{
    public static string Sealed(Guid photoId, string uploadKey) =>
        $"shelf-bags/{photoId:N}{Path.GetExtension(uploadKey)}";
}
