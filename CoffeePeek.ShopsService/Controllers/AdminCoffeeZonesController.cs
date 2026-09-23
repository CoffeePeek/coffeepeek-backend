using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/coffee-zones")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class AdminCoffeeZonesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? cityId, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminCoffeeZoneDto[]>>(
            new GetAdminCoffeeZonesQuery(cityId), ct);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(
            new GetAdminCoffeeZoneQuery(id), ct);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCoffeeZoneCommand command, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(command, ct);
        if (!response.IsSuccess)
            return ToActionResult(response);
        return CreatedAtAction(nameof(Get), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCoffeeZoneRequest request, CancellationToken ct)
    {
        var command = new UpdateCoffeeZoneCommand(
            id, request.CityId, request.Name, request.Description,
            request.CenterLatitude, request.CenterLongitude, request.RadiusMeters);
        return ToActionResult(await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(command, ct));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetCoffeeZoneStatusRequest request, CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(
            new SetCoffeeZoneStatusCommand(id, request.Status), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(
            new SetCoffeeZoneStatusCommand(id, CoffeeZoneStatus.Archived), ct));

    [HttpGet("{id:guid}/membership")]
    public async Task<IActionResult> PreviewMembership(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<CoffeeZoneMembershipPreviewDto>>(
            new PreviewCoffeeZoneMembershipQuery(id), ct);
        return ToActionResult(response);
    }

    [HttpPut("{zoneId:guid}/membership/{shopId:guid}")]
    public async Task<IActionResult> SetMembershipOverride(
        Guid zoneId,
        Guid shopId,
        [FromBody] SetCoffeeZoneMembershipOverrideRequest request,
        CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Response<CoffeeZoneMembershipPreviewDto>>(
            new SetCoffeeZoneMembershipOverrideCommand(zoneId, shopId, request.Kind), ct));

    [HttpDelete("{zoneId:guid}/membership/{shopId:guid}")]
    public async Task<IActionResult> DeleteMembershipOverride(Guid zoneId, Guid shopId, CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Response<CoffeeZoneMembershipPreviewDto>>(
            new DeleteCoffeeZoneMembershipOverrideCommand(zoneId, shopId), ct));

    [HttpPost("candidates")]
    public async Task<IActionResult> GenerateCandidates(
        [FromBody] GenerateCoffeeZoneCandidatesRequest request,
        CancellationToken ct)
    {
        var query = new GenerateCoffeeZoneCandidatesQuery(request.CityId, request.RadiusMeters, request.MinShops);
        var response = await bus.InvokeAsync<Response<CoffeeZoneCandidateDto[]>>(query, ct);
        return Ok(response);
    }

    private IActionResult ToActionResult<T>(Response<T> response)
    {
        if (response.IsSuccess)
            return Ok(response);
        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }
}

public sealed record UpdateCoffeeZoneRequest(
    Guid CityId,
    string Name,
    string? Description,
    [Range(-90, 90)] decimal CenterLatitude,
    [Range(-180, 180)] decimal CenterLongitude,
    [Range(BusinessConstants.MinCoffeeZoneRadiusMeters, BusinessConstants.MaxCoffeeZoneRadiusMeters)] int RadiusMeters);

public sealed record SetCoffeeZoneStatusRequest(CoffeeZoneStatus Status);
public sealed record SetCoffeeZoneMembershipOverrideRequest(CoffeeZoneMembershipOverrideKind Kind);
public sealed record GenerateCoffeeZoneCandidatesRequest(
    Guid CityId,
    [Range(BusinessConstants.MinCoffeeZoneRadiusMeters, BusinessConstants.MaxCoffeeZoneRadiusMeters)] int RadiusMeters = 400,
    [Range(3, 50)] int MinShops = 4);
