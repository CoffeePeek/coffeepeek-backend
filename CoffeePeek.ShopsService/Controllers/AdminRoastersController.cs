using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global roaster catalog.</summary>
[ApiController]
[Route("api/admin/roasters")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminRoastersController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<RoasterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRoasterCommand command,
        CancellationToken ct)
    {
        var commandWithActor = command with { ActorUserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<Response<RoasterDto>>(commandWithActor, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<RoasterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateRoasterRequest request,
        CancellationToken ct)
    {
        var command = new UpdateRoasterCommand(
            id,
            request.Name,
            request.About,
            request.CityId,
            request.Address,
            request.Latitude,
            request.Longitude,
            request.InstagramLink,
            request.SiteLink,
            request.Photos,
            userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<RoasterDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteRoasterCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateRoasterRequest(
    string Name,
    string? About = null,
    Guid? CityId = null,
    string? Address = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? InstagramLink = null,
    string? SiteLink = null,
    List<UploadedPhotoDto>? Photos = null);
