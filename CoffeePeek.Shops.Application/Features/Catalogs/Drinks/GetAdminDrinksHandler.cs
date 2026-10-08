using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public static class GetAdminDrinksHandler
{
    public static async Task<Response<AdminDrinkDto[]>> Handle(GetAdminDrinksQuery query,
        ICoffeeDrinkRepository catalog, CancellationToken ct) =>
        Response<AdminDrinkDto[]>.Success((await catalog.GetAllAsync(ct)).Select(AdminDrinkDto.From).ToArray());
}
