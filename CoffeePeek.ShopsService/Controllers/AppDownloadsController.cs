using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public app download configuration for CoffeePeek clients.</summary>
[ApiController]
[Route("api/v1/app-downloads")]
[AllowAnonymous]
[Tags("App Downloads")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AppDownloadsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<Response<AppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AppDownloadsDto>>(new GetPublicAppDownloadsQuery(), ct);
        return Ok(response);
    }
}
