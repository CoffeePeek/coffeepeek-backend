using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetMenuDrinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/menu")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Tags("Catalogs")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class MenuController(IMessageBus bus) : ControllerBase
{
    [HttpGet("drinks")]
    [ProducesResponseType(typeof(Response<GetMenuDrinksResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDrinks(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetMenuDrinksResponse>>(new GetMenuDrinksQuery(), ct);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
