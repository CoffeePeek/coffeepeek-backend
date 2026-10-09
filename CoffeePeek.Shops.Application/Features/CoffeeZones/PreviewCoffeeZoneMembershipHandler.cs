using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class PreviewCoffeeZoneMembershipHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        PreviewCoffeeZoneMembershipQuery query, ICoffeeZoneQueries queries, CancellationToken ct)
    {
        var preview = await queries.PreviewMembershipAsync(query.Id, ct);
        return preview is null
            ? GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>()
            : Response<CoffeeZoneMembershipPreviewDto>.Success(preview);
    }
}
