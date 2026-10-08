using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public static class CreateCatalogDrinkHandler
{
    public static async Task<Response<AdminDrinkDto>> Handle(CreateCatalogDrinkCommand command,
        ICoffeeDrinkRepository catalog, IMessageBus bus, CancellationToken ct)
    {
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), command.Slug,
            command.NameRu, command.NameEn, command.Category, command.Aliases, command.SortOrder, command.IsActive);
        if (await catalog.FindTrackedAsync(drink.Slug, ct) is not null)
            throw new ConflictException("Drink slug already exists.", statusCode: 409);
        catalog.Add(drink);
        await bus.PublishAsync(new DrinkCatalogCacheInvalidationRequested());
        return Response<AdminDrinkDto>.Success(AdminDrinkDto.From(drink));
    }
}
