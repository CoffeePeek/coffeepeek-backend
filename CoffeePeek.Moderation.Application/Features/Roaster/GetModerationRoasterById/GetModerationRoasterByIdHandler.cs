using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Roaster.GetModerationRoasterById;

public static class GetModerationRoasterByIdHandler
{
    public static async Task<Response<ModerationRoasterDto>> Handle(
        GetModerationRoasterByIdQuery query,
        IQueryModerationRoasterRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var roaster = await repository.GetById(query.Id, ct);
        if (roaster is null)
            return Response<ModerationRoasterDto>.Error("Roaster not found");

        return Response<ModerationRoasterDto>.Success(mapper.Map<ModerationRoasterDto>(roaster));
    }
}
