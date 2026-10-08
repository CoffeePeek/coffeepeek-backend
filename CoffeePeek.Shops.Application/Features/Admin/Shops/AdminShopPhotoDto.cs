namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

/// <summary>Gallery photo metadata returned on admin/owner published shop responses.</summary>
public record AdminShopPhotoDto(
    Guid Id,
    string FileName,
    string ContentType,
    string StorageKey,
    string FullUrl,
    long SizeBytes,
    int SortIndex);
