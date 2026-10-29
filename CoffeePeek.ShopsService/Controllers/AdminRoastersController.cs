using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
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
    /// <summary>Full list with aggregate GUIDs (the public /api/Catalogs lists expose only slugs).</summary>
    [HttpGet]
    [ProducesResponseType<Response<GetAllRoastersResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<GetAllRoastersResponse>>(new GetAllRoastersCommand(), ct));

    /// <summary>Editable roaster profile by its administrative UUID, including the city identity.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<RoasterDetailsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<RoasterDetailsDto>>(new GetRoasterByIdQuery(id), ct);
        return response is { IsSuccess: true, Data: not null } ? Ok(response) : NotFound();
    }

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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateRoasterRequest request,
        CancellationToken ct,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (!ShopProfileRevisionHeader.TryParse(ifMatch, out var revision))
            return BadRequest(Response<RoasterDto>.Error(System.Net.HttpStatusCode.BadRequest,
                "If-Match must contain a nonnegative profile revision."));
        var command = request.ToCommand(id, userContext.GetUserIdOrThrow(), revision);
        var response = await bus.InvokeAsync<Response<RoasterDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPatch("{id:guid}/owner")]
    [Authorize(Policy = RoleConsts.Admin)]
    [ProducesResponseType<Response<BusinessOwnershipTargetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignOwner(Guid id, [FromBody] AssignRoasterOwnerRequest request, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<BusinessOwnershipTargetDto>>(
            new AssignBusinessOwnerCommand(BusinessType.Roaster, id, request.OwnerUserId,
                userContext.GetUserIdOrThrow(), request.Reason), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
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
