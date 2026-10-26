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
            items.Where(x => x.ShopId.HasValue).Select(x => x.ShopId!.Value).Where(id => id != Guid.Empty).Distinct().ToArray(), ct))
            .ToDictionary(x => x.EntityId, PublicAddressDto.From);
        var authors = (await profiles.GetProfilesByIdsAsync(items.Select(x => x.UserId).Where(id => id != Guid.Empty).Distinct().ToArray(), ct))
            .ToDictionary(x => x.Address.EntityId);
        return items.Select(x => new PublicCheckInDto
        {
            Id = x.Id, Kind = x.Kind, Home = x.Home, Shop = x.ShopId.HasValue ? shops.GetValueOrDefault(x.ShopId.Value) : null, Author = authors.TryGetValue(x.UserId, out var author) ? PublicAddressDto.From(author.Address) : null,
            AuthorId = author?.AuthorId,
            AuthorAvatarUrl = author?.AvatarUrl,
            Username = x.Username, ShopName = x.ShopName, Text = x.Text, Rating = x.Rating,
            VisitedAt = x.VisitedAt, CreatedAtUtc = x.CreatedAtUtc,
            Visibility = x.Visibility, ModerationState = x.ModerationState, ContentRevision = x.ContentRevision,
            RejectionReason = x.RejectionReason, DrinkSlug = x.Drink?.Slug, CustomDrinkName = x.Drink?.CustomName,
            DrinkNameRu = x.Drink?.NameRu, DrinkNameEn = x.Drink?.NameEn, Photos = x.Photos, DrinkPhotoUrl = x.DrinkPhoto?.Url,
            DrinkPhotoVariants = x.DrinkPhoto?.Variants,
            HelpfulCount = x.HelpfulCount, IsHelpfulByCurrentUser = x.IsHelpfulByCurrentUser,
            CountsForLeaderboard = x.CountsForLeaderboard, LeaderboardStatus = x.LeaderboardStatus
        }).ToArray();
    }
}
