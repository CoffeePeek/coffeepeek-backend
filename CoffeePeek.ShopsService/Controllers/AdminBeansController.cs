using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global coffee bean catalog.</summary>
[ApiController]
[Route("api/admin/beans")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminBeansController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<CoffeeBeansDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCoffeeBeanCommand command,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<CoffeeBeansDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<CoffeeBeansDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCoffeeBeanRequest request,
        CancellationToken ct)
    {
        var command = new UpdateCoffeeBeanCommand(id, request.Name);
        var response = await bus.InvokeAsync<Response<CoffeeBeansDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteCoffeeBeanCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateCoffeeBeanRequest(string Name);
