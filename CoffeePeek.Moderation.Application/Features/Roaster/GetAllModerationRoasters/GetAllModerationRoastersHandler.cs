using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Roaster.GetAllModerationRoasters;

public static class GetAllModerationRoastersHandler
{
    public static async Task<Response<GetAllModerationRoastersResponse>> Handle(
        GetAllModerationRoastersQuery query,
        IQueryModerationRoasterRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        ModerationStatus? domainStatus = query.Status.HasValue
            ? (ModerationStatus?)query.Status.Value
            : null;

        var (items, totalCount) = await repository.GetPagedForReviewAsync(
            query.Page, query.PageSize, domainStatus, ct);

        var dtos = mapper.Map<ModerationRoasterDto[]>(items);
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return Response<GetAllModerationRoastersResponse>.Success(new GetAllModerationRoastersResponse(
            dtos, totalCount, totalPages, query.Page, query.PageSize));
    }
}
