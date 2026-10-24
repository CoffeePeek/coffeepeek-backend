using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/cities")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalCitiesController(
    IQueryCityRepository cities, IOptions<GatewayAuthOptions> options) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey))
            return Unauthorized();
        var city = await cities.GetById(id, ct);
        return city is null ? NotFound() : Ok(new CityDto { Id = city.Id, Name = city.Name });
    }
}
