using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetMenuDrinks;

public static class GetMenuDrinksHandler
{
    public static async Task<Response<GetMenuDrinksResponse>> Handle(
        GetMenuDrinksQuery _,
        IQueryCoffeeDrinkRepository repository,
        CancellationToken ct)
    {
        var active = await repository.GetActiveAsync(ct);
        var drinks = active.Select(ShopMenuDtoFactory.ToDto).ToArray();
        return Response<GetMenuDrinksResponse>.Success(new GetMenuDrinksResponse(drinks));
    }
}
