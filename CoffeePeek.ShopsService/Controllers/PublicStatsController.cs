using CoffeePeek.Contract.Dtos.Public;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public platform statistics for the marketing landing page.</summary>
[ApiController]
[Route("api/public/stats")]
[AllowAnonymous]
[Tags("Public")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class PublicStatsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Returns aggregate counts for published coffee shops, reviews, check-ins,
    /// and the average review rating.
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<Response<PublicPlatformStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<PublicPlatformStatsDto>>(
            new GetPublicStatsQuery(),
            cancellationToken);

        return Ok(response);
    }
}
