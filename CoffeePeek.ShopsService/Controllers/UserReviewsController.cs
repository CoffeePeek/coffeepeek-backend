using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.GetReviewsByUserId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/users/{slug}/reviews")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UserReviewsController(IMessageBus bus, IProfileAddressLookup profiles, PublicDiscoveryService discovery) : ControllerBase
{
    /// <summary>
    /// Get reviews for user
    /// </summary>
    /// <param name="slug"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PublicPage<PublicReviewDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserReviews(
        string slug,
        [FromQuery] int pageNumber = 1,
        [FromQuery][Range(1, 100)] int pageSize = 10,
        CancellationToken ct = default)
    {
        var address = await profiles.ResolveAsync(slug, ct);
        if (address is null) return NotFound();

        pageNumber = Math.Clamp(pageNumber, 1, 10_000); // larger values overflow Skip((page-1)*size)
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = new GetReviewsByUserIdQuery(address.EntityId, pageNumber, pageSize);
        var response = await bus.InvokeAsync<Response<GetReviewsByUserIdResponse>>(query, ct);

        if (response is { IsSuccess: true, Data: not null })
        {
            Response.Headers["X-Total-Count"] = response.Data.TotalItems.ToString();
            Response.Headers["X-Total-Pages"] = response.Data.TotalPages.ToString();
            Response.Headers["X-Current-Page"] = response.Data.CurrentPage.ToString();
            Response.Headers["X-Page-Size"] = response.Data.PageSize.ToString();
        }

        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(CoffeePeek.Shared.Kernel.Response.Response.Error(400, response.Message));
        var data = response.Data;
        return Ok(Response<PublicPage<PublicReviewDto>>.Success(new(await discovery.ReviewsAsync(data.ReviewDtos, ct),
            data.TotalItems, data.TotalPages, data.CurrentPage, data.PageSize)));
    }
}
