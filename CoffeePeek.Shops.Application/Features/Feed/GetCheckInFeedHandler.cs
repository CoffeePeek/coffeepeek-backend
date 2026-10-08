using CoffeePeek.Contract.Dtos.Feed;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;
using Wolverine.Attributes;

namespace CoffeePeek.Shops.Application.Features.Feed;

public static class GetCheckInFeedHandler
{
    [NonTransactional]
    public static async Task<Response<CheckInFeedPageDto>> Handle(GetCheckInFeedQuery query,
        ICheckInFeedReader reader, IPublicAddressService addresses, IProfileAddressLookup profiles,
        IPublicCheckInProjector projector, CancellationToken ct)
    {
        query.Validate();
        var cityId = query.CitySlug is null ? (Guid?)null : await ResolveAsync(PublicAddressKind.City, query.CitySlug);
        var shopId = query.CoffeeShopSlug is null ? (Guid?)null : await ResolveAsync(PublicAddressKind.CoffeeShop, query.CoffeeShopSlug);
        var authorId = query.AuthorSlug is null ? (Guid?)null
            : (await profiles.ResolveAsync(query.AuthorSlug, ct))?.EntityId
                ?? throw new NotFoundException("Feed author not found.");
        var scope = new CheckInFeedScope(cityId, shopId, authorId);
        var after = query.Cursor is null ? null : CheckInFeedCursor.Decode(query.Cursor, scope);
        var page = await reader.ReadAsync(scope, query.PageSize, after, query.ReaderId, ct);
        var checkIns = await projector.ProjectAsync(page.Items.Select(x => x.CheckIn).ToArray(), ct);
        var items = checkIns.Select((checkIn, i) => new CheckInFeedItemDto(page.Items[i].PublishedAtUtc, checkIn)).ToArray();
        var cursor = page.HasMore && page.Items.Length > 0
            ? CheckInFeedCursor.Encode(new(page.Items[^1].PublishedAtUtc, page.Items[^1].CheckIn.Id), scope) : null;
        return Response<CheckInFeedPageDto>.Success(new(items, cursor));

        async Task<Guid> ResolveAsync(PublicAddressKind kind, string slug) =>
            (await addresses.ResolveAsync(kind, slug, ct))?.EntityId ?? throw new NotFoundException("Feed filter not found.");
    }
}
