using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Coffee Shops Management")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CoffeeShopsController(PublicDiscoveryService discovery, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Search for coffee shops with advanced filters
    /// </summary>
    /// <remarks>
    /// This endpoint allows you to search for coffee shops using various filters like city, equipment, beans, and more.
    /// Results are paginated and include metadata in X-Headers.
    /// </remarks>
    /// <response code="200">Returns list of coffee shops. Check X-Total-Count header for pagination metadata.</response>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PublicCoffeeShopsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCoffeeShops(
        [FromQuery] string? city = null,
        [FromQuery, MaxLength(100)] string? q = null,
        [FromQuery] string[]? roasters = null,
        [FromQuery] string[]? equipments = null,
        [FromQuery] string[]? beans = null,
        [FromQuery] string[]? brewMethods = null,
        [FromQuery] string[]? tags = null,
        [FromQuery] bool? isOpen = null,
        [FromQuery] bool? isNew = null,
        [FromQuery] bool? isVisited = null,
        [FromQuery] Contract.Enums.PriceRange? priceRange = null,
        [FromQuery] Contract.Enums.CoffeeShopType? type = null,
        [FromQuery][Range(0, 5)] decimal? minRating = null,
        [FromQuery][Range(1, 10_000)] int page = 1, // (page-1)*size overflowed int and made Skip throw (500)
        [FromQuery][Range(1, 100)] int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var query = new SearchCoffeeShopsQuery(
            UserId: userContext.GetUserId(),
            Query: q,
            IsOpen: isOpen,
            IsNew: isNew,
            IsVisited: isVisited,
            PriceRange: priceRange,
            Type: type,
            MinRating: minRating,
            PageNumber: page,
            PageSize: pageSize);

        var response = Response<PublicCoffeeShopsResponse>.Success(await discovery.SearchAsync(query, city, roasters, equipments, beans, brewMethods, tags, ct));

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
    /// <param name="slug">Canonical public slug or an existing alias.</param>
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
