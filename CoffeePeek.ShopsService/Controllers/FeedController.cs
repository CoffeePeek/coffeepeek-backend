using CoffeePeek.Contract.Dtos.Feed;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/feed")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class FeedController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>Latest approved public check-ins, ordered by first publication; optional authentication personalizes helpful votes.</summary>
    [HttpGet]
    [ProducesResponseType<Response<CheckInFeedPageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromQuery] string? citySlug = null,
        [FromQuery] string? coffeeShopSlug = null, [FromQuery] string? authorSlug = null,
        [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var response = await bus.InvokeAsync<Response<CheckInFeedPageDto>>(new GetCheckInFeedQuery(
            userContext.GetUserId(), citySlug, coffeeShopSlug, authorSlug, pageSize, cursor), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
    }
}
