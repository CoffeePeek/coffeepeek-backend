using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.Contract.Events;

/// <summary>Committed Shops content has claimed these uploads; Media can finalize them idempotently.</summary>
public record ShopPhotosAttachedEvent(
    Guid OwnerId,
    Guid ShopId,
    ShopPhotoAttachmentKind Kind,
    IReadOnlyList<string> StorageKeys);
