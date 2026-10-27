using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf;

public sealed record ShelfBagPhotoClaimResult(ShelfBagPhotoClaimStatus Status, ClaimedShelfBagPhotoDto? Photo = null);
