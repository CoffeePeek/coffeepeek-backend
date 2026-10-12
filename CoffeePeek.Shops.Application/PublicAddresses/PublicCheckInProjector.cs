using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed class PublicCheckInProjector(IPublicAddressService addresses, IProfileAddressLookup profiles)
    : IPublicCheckInProjector
{
    public async Task<PublicCheckInDto[]> ProjectAsync(UnifiedCheckInDto[] items, CancellationToken ct)
    {
        if (items.Length == 0) return [];
        var shops = (await addresses.GetForReadAsync(PublicAddressKind.CoffeeShop,
            items.Select(x => x.ShopId).Where(id => id != Guid.Empty).Distinct().ToArray(), ct))
            .ToDictionary(x => x.EntityId, PublicAddressDto.From);
        var authors = (await profiles.GetByIdsAsync(items.Select(x => x.UserId).Distinct().ToArray(), ct))
            .ToDictionary(x => x.EntityId, PublicAddressDto.From);
        return items.Select(x => new PublicCheckInDto
        {
            Id = x.Id, Shop = shops.GetValueOrDefault(x.ShopId), Author = authors.GetValueOrDefault(x.UserId),
            Username = x.Username, ShopName = x.ShopName, Text = x.Text, Rating = x.Rating,
            VisitedAt = x.VisitedAt, CreatedAtUtc = x.CreatedAtUtc,
            Visibility = x.Visibility, ModerationState = x.ModerationState, ContentRevision = x.ContentRevision,
            RejectionReason = x.RejectionReason, DrinkSlug = x.Drink?.Slug, CustomDrinkName = x.Drink?.CustomName,
            DrinkNameRu = x.Drink?.NameRu, DrinkNameEn = x.Drink?.NameEn, Photos = x.Photos, DrinkPhotoUrl = x.DrinkPhoto?.Url,
            HelpfulCount = x.HelpfulCount, IsHelpfulByCurrentUser = x.IsHelpfulByCurrentUser
        }).ToArray();
    }
}
