using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentResults;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

/// <summary>
/// Applies optional profile patches to a published shop. Failed <see cref="Result"/> messages
/// are validation errors for the caller to map to HTTP 400.
/// </summary>
public static class ShopProfileApplier
{
    public static async Task<Result> ApplyAsync(
        DomainCoffeeShop shop,
        ShopProfilePatch patch,
        IQueryCityRepository cities,
        IQueryEquipmentRepository equipment,
        IQueryCoffeeBeanRepository beans,
        IQueryRoasterRepository roasters,
        IQueryBrewMethodRepository brewMethods,
        CancellationToken ct)
    {
        if (patch.Location is not null)
        {
            if (!await cities.Exists(patch.Location.CityId, ct))
                return Result.Fail("City was not found.");

            try
            {
                shop.SetLocation(
                    patch.Location.CityId,
                    patch.Location.Address,
                    patch.Location.Latitude,
                    patch.Location.Longitude);
            }
            catch (DomainException ex)
            {
                return Result.Fail(ex.Message);
            }
        }

        if (patch.Contacts is not null)
        {
            try
            {
                shop.SetContact(
                    patch.Contacts.InstagramLink,
                    patch.Contacts.Email,
                    patch.Contacts.SiteLink,
                    patch.Contacts.PhoneNumber);
            }
            catch (DomainException ex)
            {
                return Result.Fail(ex.Message);
            }
        }

        if (patch.Schedules is not null)
        {
            var schedules = new List<ShopSchedule>();
            foreach (var day in patch.Schedules)
            {
                var intervals = (day.Intervals ?? [])
                    .Select(i => ShopScheduleInterval.Create(i.OpenTime, i.CloseTime))
                    .ToList();
                schedules.Add(ShopSchedule.Create(day.DayOfWeek, day.IsClosed, intervals));
            }

            var replaced = shop.ReplaceSchedules(schedules);
            if (replaced.IsFailed)
                return replaced;
        }

        if (patch.Catalogs is not null)
            return await ApplyCatalogsAsync(
                shop, patch.Catalogs, equipment, beans, roasters, brewMethods, ct);

        return Result.Ok();
    }

    private static async Task<Result> ApplyCatalogsAsync(
        DomainCoffeeShop shop,
        ShopCatalogsPatch catalogs,
        IQueryEquipmentRepository equipment,
        IQueryCoffeeBeanRepository beans,
        IQueryRoasterRepository roasters,
        IQueryBrewMethodRepository brewMethods,
        CancellationToken ct)
    {
        if (catalogs.EquipmentIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.EquipmentIds,
                ids => equipment.GetByIds(ids, ct),
                "equipment");
            if (loaded.IsFailed)
                return loaded.ToResult();
            shop.SetEquipment(loaded.Value);
        }

        if (catalogs.BeanIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.BeanIds,
                ids => beans.GetByIds(ids, ct),
                "coffee beans");
            if (loaded.IsFailed)
                return loaded.ToResult();
            shop.SetBeans(loaded.Value);
        }

        if (catalogs.RoasterIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.RoasterIds,
                ids => roasters.GetByIds(ids, ct),
                "roasters");
            if (loaded.IsFailed)
                return loaded.ToResult();
            shop.SetRoasters(loaded.Value);
        }

        if (catalogs.BrewMethodIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.BrewMethodIds,
                ids => brewMethods.GetByIds(ids, ct),
                "brew methods");
            if (loaded.IsFailed)
                return loaded.ToResult();
            shop.SetBrewMethods(loaded.Value);
        }

        return Result.Ok();
    }

    private static async Task<Result<IReadOnlyList<T>>> LoadDistinctAsync<T>(
        IReadOnlyList<Guid> ids,
        Func<List<Guid>, Task<IEnumerable<T>>> load,
        string catalogName)
        where T : class
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
            return Result.Ok<IReadOnlyList<T>>([]);

        var items = (await load(distinct)).ToList();
        if (items.Count != distinct.Count)
            return Result.Fail($"One or more {catalogName} IDs were not found.");

        return Result.Ok<IReadOnlyList<T>>(items);
    }
}
