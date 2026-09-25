using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using DomainCoffeeFocus = CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus;

namespace CoffeePeek.Moderation.Application.Features.Shop.GetAllModerationShops;

public static class GetAllModerationShopsHandler
{
    public static async Task<Response<GetAllModerationShopsResponse>> Handle(
        GetAllModerationShopsQuery query,
        IQueryModerationShopRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        ModerationStatus? domainStatus = query.Status.HasValue
            ? (ModerationStatus?)query.Status.Value
            : null;

        DomainCoffeeFocus? domainFocus = query.Type.HasValue
            ? (DomainCoffeeFocus)(int)query.Type.Value
            : null;

        var (items, totalCount) = await repository.GetPagedForReviewAsync(
            query.Page,
            query.PageSize,
            domainStatus,
            query.Search,
            domainFocus,
            cancellationToken: ct);

        var dtos = mapper.Map<ModerationShopDto[]>(items);
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return Response<GetAllModerationShopsResponse>.Success(new GetAllModerationShopsResponse(
            dtos,
            totalCount,
            totalPages,
            query.Page,
            query.PageSize));
    }
}
