using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global brew method catalog.</summary>
[ApiController]
[Route("api/admin/brew-methods")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminBrewMethodsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<BrewMethodDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBrewMethodCommand command,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<BrewMethodDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<BrewMethodDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBrewMethodRequest request,
        CancellationToken ct)
    {
        var command = new UpdateBrewMethodCommand(id, request.Name, request.Category);
        var response = await bus.InvokeAsync<Response<BrewMethodDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteBrewMethodCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateBrewMethodRequest(string Name, BrewMethodCategoryEnum Category);
