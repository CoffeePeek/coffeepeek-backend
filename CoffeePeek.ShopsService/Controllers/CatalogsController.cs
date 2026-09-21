using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetShopTags;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class CatalogsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("cities")]
    [ProducesResponseType<Response<GetCitiesResponse>>(statusCode:StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCities()
    {
        var command = new GetCitiesCommand();
        var response = await bus.InvokeAsync<Response<GetCitiesResponse>>(command);
        
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("beans")]
    [ProducesResponseType(typeof(Response<GetAllCoffeeBeansResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBeans()
    {
        var response = await bus.InvokeAsync<Response<GetAllCoffeeBeansResponse>>(new GetAllBeansCommand());
        
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("equipments")]
    [ProducesResponseType(typeof(Response<GetAllEquipmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetEquipment()
    {
        var response = await bus.InvokeAsync<Response<GetAllEquipmentResponse>>(new GetAllEquipmentCommand());
        
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("roasters")]
    [ProducesResponseType(typeof(Response<GetAllRoastersResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRoasters()
    {
        var response = await bus.InvokeAsync<Response<GetAllRoastersResponse>>(new GetAllRoastersCommand());
        
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("brew-methods")]
    [ProducesResponseType(typeof(Response<GetAllBrewMethodsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType( StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBrewMethods()
    {
        var response = await bus.InvokeAsync<Response<GetAllBrewMethodsResponse>>(new GetAllBrewMethodsCommand());
        
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("shop-tags")]
    [ProducesResponseType(typeof(Response<GetShopTagsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetShopTags(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetShopTagsResponse>>(new GetShopTagsCommand(), ct);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }
}