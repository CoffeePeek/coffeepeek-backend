using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/[controller]")]
[Tags("Coffee Shops Management")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]

public class CoffeeShopsController(PublicDiscoveryService discovery, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Legacy coffee shop picker for check-in creation (deprecated).
    /// </summary>
    /// <remarks>
    /// Supports city, q (name or address), page and pageSize only; other parameters return 400.
    /// Deprecated since 2026-10-26. Migrate to POST /api/v1/coffee-shops/search.
    /// The existing response and pagination headers are retained for check-in clients.
    /// </remarks>
    /// <response code="200">Returns list of coffee shops. Check X-Total-Count header for pagination metadata.</response>
    [HttpGet]
    [Obsolete("Legacy check-in picker. Migrate to POST /api/v1/coffee-shops/search. Removal: API-001 in docs/api-deprecations.md.")]
    [ProducesResponseType(typeof(Response<PublicCoffeeShopsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCoffeeShops(
        [FromQuery] string? city = null,
        [FromQuery, MaxLength(100)] string? q = null,
        [FromQuery][Range(1, 10_000)] int page = 1, // (page-1)*size overflowed int and made Skip throw (500)
        [FromQuery][Range(1, 100)] int pageSize = 10,
        CancellationToken ct = default)
    {
        Response.Headers["Deprecation"] = "@1792972800";
        Response.Headers["Link"] = "</api/v1/coffee-shops/search>; rel=\"successor-version\"";
        var unsupported = Request.Query.Keys
            .Except(["city", "q", "page", "pageSize"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(key => key, _ => new[] { "Use POST /api/v1/coffee-shops/search for advanced filters." });
        if (unsupported.Count > 0)
            return BadRequest(new ErrorResponse("Unsupported picker parameters.", "INVALID_FILTER", unsupported) { StatusCode = 400 });

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var query = new SearchCoffeeShopsQuery(
            UserId: userContext.GetUserId(),
            Query: q,
            PageNumber: page,
            PageSize: pageSize);

        var response = Response<PublicCoffeeShopsResponse>.Success(await discovery.SearchAsync(query, city, ct));

        if (response is { IsSuccess: true, Data: not null })
        {
            AddPaginationHeaders(response.Data);
        }

        return Ok(response);

        void AddPaginationHeaders(PublicCoffeeShopsResponse data)
        {
            Response.Headers.TryAdd("X-Total-Count", data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", data.PageSize.ToString());
        }
    }

    /// <summary>
    /// Get detailed information about a specific coffee shop
    /// </summary>
    /// <remarks>
    /// This endpoint returns full coffee shop details including:
    /// - Average rating and review count
    /// - Top 10 recent reviews
    /// - Personal user interaction status (IsVisited) if authenticated
    /// </remarks>
    /// <param name="slug">The current public slug.</param>
    /// <returns>Returns coffee shop details or an error message</returns>
    [HttpGet("{slug}")]
    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType(typeof(Response<PublicShopDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCoffeeShop(string slug, CancellationToken ct)
    {
        var data = await discovery.GetShopAsync(slug, userContext.GetUserId(), ct);
        return data is null ? NotFound() : Ok(Response<PublicShopDetailsDto>.Success(data));
    }
}
