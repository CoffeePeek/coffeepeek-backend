using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using ContractGrind = CoffeePeek.Contract.Enums.ShelfBagGrind;
using ContractStatus = CoffeePeek.Contract.Enums.ShelfBagStatus;

namespace CoffeePeek.Shops.Application.Features.Shelf;

public static class ShelfBagMapping
{
    public static string PhotoUrl(Guid bagId, Guid photoId) => $"/api/v1/shelf/bags/{bagId}/photos/{photoId}";

    public static ShelfBagDto ToDto(ShelfBag bag, IReadOnlyDictionary<Guid, string> publishedSlugs) => new()
    {
        Id = bag.Id,
        Status = (ContractStatus)bag.Status,
        Position = bag.Status == ShelfBagStatus.OnShelf ? bag.Position : null,
        Photo = bag.PhotoId is { } photoId ? new ShelfBagPhotoDto(photoId, PhotoUrl(bag.Id, photoId)) : null,
        CoffeeProductId = bag.CoffeeProductId,
        CoffeeProductSlug = bag.CoffeeProductId is { } productId && publishedSlugs.TryGetValue(productId, out var slug) ? slug : null,
        Name = bag.Name,
        RoasterName = bag.RoasterName,
        WeightGrams = bag.WeightGrams,
        Grind = bag.Grind is { } grind ? (ContractGrind)grind : null,
        RoastedOn = bag.RoastedOn,
        AddedAtUtc = DateTime.SpecifyKind(bag.AddedAtUtc, DateTimeKind.Utc),
        FinishedAtUtc = bag.FinishedAtUtc is { } finished ? DateTime.SpecifyKind(finished, DateTimeKind.Utc) : null,
        Rating = bag.Rating,
        BuyAgain = bag.BuyAgain
    };

    public static async Task<ShelfBagDto[]> ToDtosAsync(IReadOnlyCollection<ShelfBag> bags, IShelfCatalogLookup catalog,
        CancellationToken ct)
    {
        var productIds = bags.Where(b => b.CoffeeProductId != null).Select(b => b.CoffeeProductId!.Value).Distinct().ToArray();
        var slugs = productIds.Length == 0 ? new Dictionary<Guid, string>() : await catalog.GetPublishedSlugsAsync(productIds, ct);
        return bags.Select(b => ToDto(b, slugs)).ToArray();
    }

    public static async Task<ShelfBagDto> ToDtoAsync(ShelfBag bag, IShelfCatalogLookup catalog, CancellationToken ct) =>
        (await ToDtosAsync([bag], catalog, ct))[0];
}
