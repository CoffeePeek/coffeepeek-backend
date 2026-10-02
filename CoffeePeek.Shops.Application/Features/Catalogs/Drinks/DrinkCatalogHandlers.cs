using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using ContractCategory = CoffeePeek.Contract.Enums.CoffeeDrinkCategory;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public record GetConsumedDrinksQuery;
public record GetAdminDrinksQuery;
public record CreateCatalogDrinkCommand(string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory Category, string Aliases, int SortOrder, bool IsActive);
public record UpdateCatalogDrinkCommand(string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory Category, string Aliases, int SortOrder, bool IsActive);
public record AdminDrinkDto(Guid Id, string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory Category, string Aliases, int SortOrder, bool IsActive)
{
    public static AdminDrinkDto From(CoffeeDrinkDefinition drink) => new(drink.Id, drink.Slug,
        drink.NameRu, drink.NameEn, drink.Category, drink.Aliases, drink.SortOrder, drink.IsActive);
}

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

public static class GetAdminDrinksHandler
{
    public static async Task<Response<AdminDrinkDto[]>> Handle(GetAdminDrinksQuery query,
        ICoffeeDrinkRepository catalog, CancellationToken ct) =>
        Response<AdminDrinkDto[]>.Success((await catalog.GetAllAsync(ct)).Select(AdminDrinkDto.From).ToArray());
}

public static class CreateCatalogDrinkHandler
{
    public static async Task<Response<AdminDrinkDto>> Handle(CreateCatalogDrinkCommand command,
        ICoffeeDrinkRepository catalog, ICacheService cache, CancellationToken ct)
    {
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), command.Slug,
            command.NameRu, command.NameEn, command.Category, command.Aliases, command.SortOrder, command.IsActive);
        if (await catalog.FindTrackedAsync(drink.Slug, ct) is not null)
            throw new ConflictException("Drink slug already exists.", statusCode: 409);
        catalog.Add(drink);
        await catalog.SaveAsync(ct);
        await cache.RemoveAsync(CacheKey.Shop.MenuDrinks());
        await cache.RemoveByPattern("shop:detail:*", ct);
        return Response<AdminDrinkDto>.Success(AdminDrinkDto.From(drink));
    }
}

public static class UpdateCatalogDrinkHandler
{
    public static async Task<Response<AdminDrinkDto>> Handle(UpdateCatalogDrinkCommand command,
        ICoffeeDrinkRepository catalog, ICacheService cache, CancellationToken ct)
    {
        var drink = await catalog.FindTrackedAsync(command.Slug, ct)
            ?? throw new NotFoundException("Drink not found.");
        drink.Update(command.NameRu, command.NameEn, command.Category, command.Aliases, command.SortOrder, command.IsActive);
        await catalog.SaveAsync(ct);
        await cache.RemoveAsync(CacheKey.Shop.MenuDrinks());
        await cache.RemoveByPattern("shop:detail:*", ct);
        return Response<AdminDrinkDto>.Success(AdminDrinkDto.From(drink));
    }
}
