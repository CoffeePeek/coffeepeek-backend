using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record GetAdminCoffeeZonesQuery(Guid? CityId);
public record GetAdminCoffeeZoneQuery(Guid Id);
public record PreviewCoffeeZoneMembershipQuery(Guid Id);
public record GenerateCoffeeZoneCandidatesQuery(Guid CityId, int RadiusMeters = 400, int MinShops = 4);

public record CreateCoffeeZoneCommand(
    Guid CityId, string Name, string? Description,
    decimal CenterLatitude, decimal CenterLongitude, int RadiusMeters);

public record UpdateCoffeeZoneCommand(
    [property: JsonIgnore] Guid Id,
    Guid CityId, string Name, string? Description,
    decimal CenterLatitude, decimal CenterLongitude, int RadiusMeters);

public record SetCoffeeZoneStatusCommand([property: JsonIgnore] Guid Id, CoffeeZoneStatus Status);

public record SetCoffeeZoneMembershipOverrideCommand(
    [property: JsonIgnore] Guid ZoneId,
    [property: JsonIgnore] Guid ShopId,
    CoffeeZoneMembershipOverrideKind Kind);

public record DeleteCoffeeZoneMembershipOverrideCommand(
    [property: JsonIgnore] Guid ZoneId,
    [property: JsonIgnore] Guid ShopId);

public static class GetAdminCoffeeZonesHandler
{
    public static async Task<Response<AdminCoffeeZoneDto[]>> Handle(
        GetAdminCoffeeZonesQuery query, ICoffeeZoneQueries queries, CancellationToken ct) =>
        Response<AdminCoffeeZoneDto[]>.Success(await queries.GetAllAsync(query.CityId, ct));
}

public static class GetAdminCoffeeZoneHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        GetAdminCoffeeZoneQuery query, ICoffeeZoneQueries queries, CancellationToken ct)
    {
        var zone = await queries.GetByIdAsync(query.Id, ct);
        return zone is null ? NotFound<AdminCoffeeZoneDto>() : Response<AdminCoffeeZoneDto>.Success(zone);
    }

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(HttpStatusCode.NotFound, "Coffee zone not found.");
}

public static class PreviewCoffeeZoneMembershipHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        PreviewCoffeeZoneMembershipQuery query, ICoffeeZoneQueries queries, CancellationToken ct)
    {
        var preview = await queries.PreviewMembershipAsync(query.Id, ct);
        return preview is null
            ? GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>()
            : Response<CoffeeZoneMembershipPreviewDto>.Success(preview);
    }
}

public static class GenerateCoffeeZoneCandidatesHandler
{
    public static async Task<Response<CoffeeZoneCandidateDto[]>> Handle(
        GenerateCoffeeZoneCandidatesQuery query, ICoffeeZoneQueries queries, CancellationToken ct) =>
        Response<CoffeeZoneCandidateDto[]>.Success(
            await queries.GenerateCandidatesAsync(query.CityId, query.RadiusMeters, query.MinShops, ct));
}

public static class CreateCoffeeZoneHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        CreateCoffeeZoneCommand command,
        IQueryCityRepository cityRepository,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (!await cityRepository.Exists(command.CityId, ct))
            return Response<AdminCoffeeZoneDto>.Error(HttpStatusCode.BadRequest, "City not found.");

        var zone = new CoffeeZone(
            command.CityId, command.Name, command.Description,
            command.CenterLatitude, command.CenterLongitude, command.RadiusMeters);
        repository.Add(zone);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<AdminCoffeeZoneDto>.Success((await queries.GetByIdAsync(zone.Id, ct))!);
    }
}

public static class UpdateCoffeeZoneHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        UpdateCoffeeZoneCommand command,
        IQueryCityRepository cityRepository,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.Id, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<AdminCoffeeZoneDto>();
        if (!await cityRepository.Exists(command.CityId, ct))
            return Response<AdminCoffeeZoneDto>.Error(HttpStatusCode.BadRequest, "City not found.");

        zone.Update(
            command.CityId, command.Name, command.Description,
            command.CenterLatitude, command.CenterLongitude, command.RadiusMeters);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<AdminCoffeeZoneDto>.Success((await queries.GetByIdAsync(zone.Id, ct))!);
    }
}

public static class SetCoffeeZoneStatusHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        SetCoffeeZoneStatusCommand command,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.Id, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<AdminCoffeeZoneDto>();

        switch (command.Status)
        {
            case CoffeeZoneStatus.Draft: zone.MoveToDraft(); break;
            case CoffeeZoneStatus.Published: zone.Publish(); break;
            case CoffeeZoneStatus.Archived: zone.Archive(); break;
            default: return Response<AdminCoffeeZoneDto>.Error(HttpStatusCode.BadRequest, "Invalid coffee zone status.");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Response<AdminCoffeeZoneDto>.Success((await queries.GetByIdAsync(zone.Id, ct))!);
    }
}

public static class SetCoffeeZoneMembershipOverrideHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        SetCoffeeZoneMembershipOverrideCommand command,
        IQueryCoffeeShopRepository shopRepository,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.ZoneId, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>();
        var shopCityId = await shopRepository.GetCityIdAsync(command.ShopId, ct);
        if (!shopCityId.HasValue)
            return Response<CoffeeZoneMembershipPreviewDto>.Error(HttpStatusCode.NotFound, "Coffee shop not found.");
        if (shopCityId.Value != zone.CityId)
            return Response<CoffeeZoneMembershipPreviewDto>.Error(
                HttpStatusCode.BadRequest, "Coffee shop and coffee zone must belong to the same city.");

        if (command.Kind == CoffeeZoneMembershipOverrideKind.Primary)
        {
            var previousPrimary = await repository.GetPrimaryOverrideAsync(command.ShopId, ct);
            if (previousPrimary is not null && previousPrimary.ZoneId != command.ZoneId)
                repository.RemoveOverride(previousPrimary);
        }

        var current = await repository.GetOverrideAsync(command.ZoneId, command.ShopId, ct);
        if (current is null)
            repository.AddOverride(new CoffeeZoneMembershipOverride(command.ZoneId, command.ShopId, command.Kind));
        else
            current.SetKind(command.Kind);

        await unitOfWork.SaveChangesAsync(ct);
        return Response<CoffeeZoneMembershipPreviewDto>.Success(
            (await queries.PreviewMembershipAsync(command.ZoneId, ct))!);
    }
}

public static class DeleteCoffeeZoneMembershipOverrideHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        DeleteCoffeeZoneMembershipOverrideCommand command,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (await repository.GetByIdAsync(command.ZoneId, ct) is null)
            return GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>();

        var current = await repository.GetOverrideAsync(command.ZoneId, command.ShopId, ct);
        if (current is not null)
        {
            repository.RemoveOverride(current);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Response<CoffeeZoneMembershipPreviewDto>.Success(
            (await queries.PreviewMembershipAsync(command.ZoneId, ct))!);
    }
}
