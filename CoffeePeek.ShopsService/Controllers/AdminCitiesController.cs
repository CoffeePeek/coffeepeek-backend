using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global city catalog.</summary>
[ApiController]
[Route("api/admin/cities")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminCitiesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<CityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCityCommand command,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<CityDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<CityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCityRequest request,
        CancellationToken ct)
    {
        var command = new UpdateCityCommand(id, request.Name);
        var response = await bus.InvokeAsync<Response<CityDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteCityCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateCityRequest(string Name);
