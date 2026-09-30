using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Map Services")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[AllowAnonymous] // This endpoint is intentionally public — authentication not required
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MapController(IMessageBus bus, PublicDiscoveryService discovery) : ControllerBase
{
    /// <summary>
    /// Get coffee shops within geographical boundaries
    /// </summary>
    /// <remarks>
    /// Use this endpoint to fetch coffee shops for map-based views. 
    /// It requires a bounding box defined by minimum and maximum coordinates.
    /// 
    /// **Constraints:**
    /// - Latitude: -90 to 90
    /// - Longitude: -180 to 180
    /// - Min values must be lower than Max values.
    /// </remarks>
    /// <param name="minLat">Minimum latitude (Southern boundary)</param>
    /// <param name="minLon">Minimum longitude (Western boundary)</param>
    /// <param name="maxLat">Maximum latitude (Northern boundary)</param>
    /// <param name="maxLon">Maximum longitude (Eastern boundary)</param>
    /// <param name="zoom">Optional map zoom. Omit it to preserve the legacy flat-marker response.</param>
    /// <response code="200">Returns a list of shops found within the specified area</response>
    /// <response code="400">Invalid coordinates or bounding box geometry</response>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PublicMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetShopsInBounds(
        [FromQuery] [Range(-90, 90)] decimal minLat,
        [FromQuery] [Range(-180, 180)] decimal minLon,
        [FromQuery] [Range(-90, 90)] decimal maxLat,
        [FromQuery] [Range(-180, 180)] decimal maxLon,
        [FromQuery] [Range(0, 22)] int? zoom = null,
        CancellationToken ct = default)
    {
        if (minLat > maxLat || minLon > maxLon)
        {
            return BadRequest(Response<GetShopsInBoundsResponse>.Error("Invalid bounds: min values must be less than or equal to max values"));
        }

        var query = new GetShopsInBoundsQuery(minLat, minLon, maxLat, maxLon, zoom);
        
        var response = await bus.InvokeAsync<Response<GetShopsInBoundsResponse>>(query, ct);
        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(CoffeePeek.Shared.Kernel.Response.Response.Error(400, response.Message));
        var data = response.Data;
        return Ok(Response<PublicMapDto>.Success(await discovery.MapAsync(data.Shops, data.Clusters, data.Zones, data.IsTruncated, ct)));
    }
}
