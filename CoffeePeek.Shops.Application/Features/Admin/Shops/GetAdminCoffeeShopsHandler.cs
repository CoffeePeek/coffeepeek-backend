using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class GetAdminCoffeeShopsHandler
{
    public static async Task<Response<GetAdminCoffeeShopsResponse>> Handle(
        GetAdminCoffeeShopsQuery query,
        IAdminCoffeeShopQueryRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await repository.GetPagedAsync(
            page, pageSize, query.Search, query.Status, ct, query.ImportedFromFile);

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var media = mediaOptions.Value;
        var dtos = items.Select(s => AdminPublishedShopMapper.Map(s, media)).ToList();

        return Response<GetAdminCoffeeShopsResponse>.Success(new GetAdminCoffeeShopsResponse(
            dtos, totalCount, totalPages, page, pageSize));
    }
}
