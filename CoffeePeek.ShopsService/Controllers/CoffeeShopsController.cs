using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Coffee Shops Management")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class CoffeeShopsController(IMessageBus bus, IUserContext userContext) : ControllerBase
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
    [ProducesResponseType(typeof(Response<GetCoffeeShopsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCoffeeShops(
        [FromQuery] Guid? cityId = null,
        [FromQuery, MaxLength(100)] string? q = null,
        [FromQuery] Guid[]? roasters = null,
        [FromQuery] Guid[]? equipments = null,
        [FromQuery] Guid[]? beans = null,
        [FromQuery] Guid[]? brewMethods = null,
        [FromQuery] Guid[]? tags = null,
        [FromQuery] bool? isOpen = null,
        [FromQuery] bool? isNew = null,
        [FromQuery] bool? isVisited = null,
        [FromQuery] Contract.Enums.PriceRange? priceRange = null,
        [FromQuery] Contract.Enums.CoffeeShopType? type = null,
        [FromQuery][Range(0, 5)] decimal? minRating = null,
        [FromQuery][Range(1, 10_000)] int page = 1, // (page-1)*size overflowed int and made Skip throw (500)
        [FromQuery][Range(1, 100)] int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var query = new SearchCoffeeShopsQuery(
            UserId: userContext.GetUserId(),
            Query: q,
            CityId: cityId,
            Roasters: roasters,
            Equipments: equipments,
            Beans: beans,
            BrewMethods: brewMethods,
            Tags: tags,
            IsOpen: isOpen,
            IsNew: isNew,
            IsVisited: isVisited,
            PriceRange: priceRange,
            Type: type,
            MinRating: minRating,
            PageNumber: page,
            PageSize: pageSize);

        var response = await bus.InvokeAsync<Response<GetCoffeeShopsResponse>>(query);

        if (response is { IsSuccess: true, Data: not null })
        {
            AddPaginationHeaders(response.Data);
        }

        return Ok(response);

        void AddPaginationHeaders(GetCoffeeShopsResponse data)
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
    /// <param name="id">The unique identifier (GUID) of the coffee shop</param>
    /// <returns>Returns coffee shop details or an error message</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<GetCoffeeShopResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCoffeeShop(Guid id)
    {
        var query = new GetCoffeeShopQuery(id, userContext.GetUserId());
        var response = await bus.InvokeAsync<Response<GetCoffeeShopResponse>>(query);
        
        return Ok(response);
    }
}