using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>
/// Public coffee zone addresses. A zone is public only while Published; Draft, Archived, unknown
/// and retired zones all return the same 404 and are omitted from batch results.
/// </summary>
[ApiController]
[Route("api/Catalogs/coffee-zones")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCoffeeZoneAddressesController(IPublicAddressService addresses, ICoffeeZoneQueries zones) : ControllerBase
{
    /// <summary>Resolves the current zone slug to the map zone data and its public address.</summary>
    [HttpGet("by-slug/{slug}")]
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.CoffeeZone, slug, ct);
        if (address is null) return NotFound();
        var zone = await zones.GetByIdAsync(address.EntityId, ct);
        // Re-check status on the data read: never return a zone archived or drafted between resolve and read.
        if (zone is null || zone.Status != CoffeeZoneStatus.Published) return NotFound();
        // Same field mapping as the /api/Map zone DTO.
        var data = new PublicZoneDto(PublicAddressDto.From(address), zone.Name, zone.Description, zone.CenterLatitude, zone.CenterLongitude,
            zone.RadiusMeters, zone.ShopCount,
            zone.Polygon.Select(p => new MapCoffeeZonePointDto(p.Latitude, p.Longitude)).ToArray(), zone.Color);
        return Ok(data);
    }

}
