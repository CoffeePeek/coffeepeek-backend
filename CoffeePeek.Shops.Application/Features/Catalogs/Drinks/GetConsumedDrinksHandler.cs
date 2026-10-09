using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using ContractCategory = CoffeePeek.Contract.Enums.CoffeeDrinkCategory;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public static class GetConsumedDrinksHandler
{
    public static async Task<Response<ConsumedDrinkOptionDto[]>> Handle(GetConsumedDrinksQuery query,
        IQueryCoffeeDrinkRepository catalog, CancellationToken ct)
    {
        var drinks = await catalog.GetActiveAsync(ct);
        return Response<ConsumedDrinkOptionDto[]>.Success([
            ..drinks.Select(d => new ConsumedDrinkOptionDto(d.Slug, d.NameRu, d.NameEn,
                (ContractCategory)(int)d.Category, d.SortOrder)),
            new(DrinkSelection.OtherSlug, "Другое", "Other", null, int.MaxValue)
        ]);
    }
}
