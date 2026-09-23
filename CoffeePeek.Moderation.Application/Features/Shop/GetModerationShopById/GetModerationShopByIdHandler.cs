using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Shop.GetModerationShopById;

public record GetModerationShopByIdQuery(Guid Id);

public static class GetModerationShopByIdHandler
{
    public static async Task<Response<ModerationShopDto>> Handle(
        GetModerationShopByIdQuery query,
        IQueryModerationShopRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var shop = await repository.GetById(query.Id, ct);
        if (shop is null)
            return Response<ModerationShopDto>.Error("Coffee shop not found");

        return Response<ModerationShopDto>.Success(mapper.Map<ModerationShopDto>(shop));
    }
}
