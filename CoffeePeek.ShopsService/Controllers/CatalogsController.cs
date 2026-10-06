using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class CatalogsController(PublicDiscoveryService discovery) : ControllerBase
{
    [HttpGet("cities")]
    [ProducesResponseType<Response<PublicCityDto[]>>(statusCode:StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCities(CancellationToken ct)
    {
        return Ok(Response<PublicCityDto[]>.Success(await discovery.CitiesAsync(ct)));
    }

    [HttpGet("beans")]
    [ProducesResponseType(typeof(Response<PublicBeanDto[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBeans(CancellationToken ct)
    {
        return Ok(Response<PublicBeanDto[]>.Success(await discovery.BeansAsync(ct)));
    }

    [HttpGet("equipments")]
    [ProducesResponseType(typeof(Response<PublicEquipmentDto[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetEquipment(CancellationToken ct)
    {
        return Ok(Response<PublicEquipmentDto[]>.Success(await discovery.EquipmentsAsync(ct)));
    }

    [HttpGet("brew-methods")]
    [ProducesResponseType(typeof(Response<PublicBrewMethodDto[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBrewMethods(CancellationToken ct)
    {
        return Ok(Response<PublicBrewMethodDto[]>.Success(await discovery.MethodsAsync(ct)));
    }

    [HttpGet("shop-tags")]
    [ProducesResponseType(typeof(Response<PublicTagDto[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetShopTags(CancellationToken ct)
    {
        return Ok(Response<PublicTagDto[]>.Success(await discovery.TagsAsync(ct)));
    }
}
