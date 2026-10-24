using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentResults;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

/// <summary>
/// Applies optional profile patches to a published shop. Failed <see cref="Result"/> messages
/// are validation errors for the caller to map to HTTP 400. A failed result leaves
/// the tracked aggregate unchanged, including its owned values and catalog links.
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
        IAddressGeocoder geocoder,
        CancellationToken ct)
    {
        // Wolverine saves tracked changes even when a handler returns a failed Response.
        // Resolve and validate the entire patch before executing any mutations.
        var changes = new List<Action>();
        List<ShopSchedule>? schedules = null;
        Location? location = null;
        if (patch.Location is not null)
        {
            if (!await cities.Exists(patch.Location.CityId, ct))
                return Result.Fail("City was not found.");

            try
            {
                location = Location.CreateValidated(
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
                var contact = ShopContact.Create(
                    patch.Contacts.InstagramLink,
                    patch.Contacts.Email,
                    patch.Contacts.SiteLink,
                    patch.Contacts.PhoneNumber);
                changes.Add(() => shop.SetContact(
                    contact.InstagramLink, contact.Email, contact.SiteLink, contact.PhoneNumber));
            }
            catch (DomainException ex)
            {
                return Result.Fail(ex.Message);
            }
        }

        if (patch.Schedules is not null)
        {
            schedules = new List<ShopSchedule>();
            foreach (var day in patch.Schedules)
            {
                var intervals = (day.Intervals ?? [])
                    .Select(i => ShopScheduleInterval.Create(i.OpenTime, i.CloseTime))
                    .ToList();
                schedules.Add(ShopSchedule.Create(day.DayOfWeek, day.IsClosed, intervals));
            }

        }

        if (patch.Catalogs is not null)
        {
            var catalogs = await PrepareCatalogsAsync(
                shop, changes, patch.Catalogs, equipment, beans, roasters, brewMethods, ct);
            if (catalogs.IsFailed)
                return catalogs;
        }

        if (location is not null)
        {
            // Existing clients send the previous pin with a new address. Only a fresh
            // geocoding result can bind those coordinates to the changed address/city.
            if (shop.Location is null || !shop.Location.IsAddressValidated ||
                shop.Location.CityId != location.CityId || shop.Location.Address != location.Address)
            {
                GeocodingResult? coordinates;
                try
                {
                    coordinates = await geocoder.GeocodeAsync(location.Address, ct);
                }
                catch (HttpRequestException ex)
                {
                    throw new ServiceUnavailableException("Address geocoding is unavailable; retry the update.", ex);
                }

                if (coordinates is null)
                    return Result.Fail("Address could not be geocoded; check the address and try again.");

                try
                {
                    location = Location.CreateValidated(location.CityId, location.Address,
                        coordinates.Latitude, coordinates.Longitude);
                }
                catch (DomainException ex)
                {
                    return Result.Fail(ex.Message);
                }
            }

            changes.Add(() => shop.SetLocation(
                location.CityId, location.Address, location.Latitude!.Value, location.Longitude!.Value));
        }

        // ReplaceSchedules validates before touching the collection. Run this last
        // fallible operation before the prepared, already validated setters.
        if (schedules is not null)
        {
            var replaced = shop.ReplaceSchedules(schedules);
            if (replaced.IsFailed)
                return replaced;
        }

        foreach (var change in changes)
            change();

        return Result.Ok();
    }

    private static async Task<Result> PrepareCatalogsAsync(
        DomainCoffeeShop shop,
        List<Action> changes,
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
            changes.Add(() => shop.SetEquipment(loaded.Value));
        }

        if (catalogs.BeanIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.BeanIds,
                ids => beans.GetByIds(ids, ct),
                "coffee beans");
            if (loaded.IsFailed)
                return loaded.ToResult();
            changes.Add(() => shop.SetBeans(loaded.Value));
        }

        if (catalogs.RoasterIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.RoasterIds,
                ids => roasters.GetByIds(ids, ct),
                "roasters");
            if (loaded.IsFailed)
                return loaded.ToResult();
            changes.Add(() => shop.SetRoasters(loaded.Value));
        }

        if (catalogs.BrewMethodIds is not null)
        {
            var loaded = await LoadDistinctAsync(
                catalogs.BrewMethodIds,
                ids => brewMethods.GetByIds(ids, ct),
                "brew methods");
            if (loaded.IsFailed)
                return loaded.ToResult();
            changes.Add(() => shop.SetBrewMethods(loaded.Value));
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
