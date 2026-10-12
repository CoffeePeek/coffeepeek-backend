using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.Shops.Application.Abstractions;

public record ShopPhotoClaimResult(
    bool Succeeded,
    IReadOnlyList<ClaimedShopPhotoDto> Photos,
    bool IsTransientFailure = false)
{
    public static ShopPhotoClaimResult Failed { get; } = new(false, []);
    public static ShopPhotoClaimResult Unreachable { get; } = new(false, [], true);
}
