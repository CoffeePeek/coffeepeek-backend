using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public static class UpdateCatalogDrinkHandler
{
    public static async Task<Response<AdminDrinkDto>> Handle(UpdateCatalogDrinkCommand command,
        ICoffeeDrinkRepository catalog, IMessageBus bus, CancellationToken ct)
    {
        var drink = await catalog.FindTrackedAsync(command.Slug, ct)
            ?? throw new NotFoundException("Drink not found.");
        drink.Update(command.NameRu, command.NameEn, command.Category, command.Aliases, command.SortOrder, command.IsActive);
        await bus.PublishAsync(new DrinkCatalogCacheInvalidationRequested());
        return Response<AdminDrinkDto>.Success(AdminDrinkDto.From(drink));
    }
}
