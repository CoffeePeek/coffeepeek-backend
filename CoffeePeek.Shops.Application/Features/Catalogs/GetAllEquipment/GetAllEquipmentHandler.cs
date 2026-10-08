using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;

public class GetAllEquipmentHandler
{
    public async Task<Response<GetAllEquipmentResponse>> Handle(
        GetAllEquipmentCommand request,
        IQueryEquipmentRepository repository,
        IMapper mapper,
        CancellationToken cancellationToken)
    {
        var equipments = await repository.GetAll(cancellationToken);
        var result = mapper.Map<EquipmentDto[]>(equipments);
        
        var response = new GetAllEquipmentResponse(result);

        return Response<GetAllEquipmentResponse>.Success(response);
    }
}
