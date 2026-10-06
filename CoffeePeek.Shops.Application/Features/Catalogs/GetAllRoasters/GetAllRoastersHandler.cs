using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;

public class GetAllRoastersHandler
{
    public async Task<Response<GetAllRoastersResponse>> Handle(
        GetAllRoastersCommand request,
        IQueryRoasterRepository repository,
        IMapper mapper,
        ICoffeeCatalogStore coffees,
        CancellationToken cancellationToken)
    {
        var roasters = await repository.GetAll();

        var roastersDto = mapper.Map<RoasterDto[]>(roasters);
        var counts = await coffees.RoasterCountsAsync(roasters.Select(r => r.Id).ToArray(), cancellationToken);
        foreach (var dto in roastersDto)
        {
            var stats = counts.GetValueOrDefault(dto.Id);
            dto.CoffeeShopsCount = stats.CoffeeShopsCount;
            dto.CoffeeProductsCount = stats.CoffeeProductsCount;
        }

        return Response<GetAllRoastersResponse>.Success(new GetAllRoastersResponse(roastersDto));
    }
}