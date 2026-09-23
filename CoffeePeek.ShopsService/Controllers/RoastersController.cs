using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public roaster profile — address, contact, photos, about, and the shops carrying it.</summary>
[ApiController]
[Route("api/roasters")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class RoastersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<RoasterDetailsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<RoasterDetailsDto>>(new GetRoasterByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}
