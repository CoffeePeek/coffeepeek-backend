using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/public-addresses/{kind:regex(^(shops|roasters|cities|coffee-zones|beans|equipments|brew-methods|shop-tags)$)}")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalPublicAddressesController(IPublicAddressService addresses, IOptions<GatewayAuthOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBatch(string kind, [FromQuery] Guid[] ids, CancellationToken ct) => Trusted()
        ? Ok(await addresses.GetByIdsAsync(Kind(kind), ids, ct)) : Unauthorized();

    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> Resolve(string kind, string slug, CancellationToken ct)
    {
        if (!Trusted()) return Unauthorized();
        var result = await addresses.ResolveAsync(Kind(kind), slug, ct);
        return result is null ? NotFound() : Ok(result);
    }

    private bool Trusted() => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey);
    [HttpGet("resolve")]
    public async Task<IActionResult> ResolveBatch(string kind, [FromQuery] string[] slugs, CancellationToken ct) => Trusted()
        ? Ok(await addresses.ResolveIdsAsync(Kind(kind), slugs, ct)) : Unauthorized();

    private static PublicAddressKind Kind(string kind) => kind switch
    {
        "shops" => PublicAddressKind.CoffeeShop, "roasters" => PublicAddressKind.Roaster,
        "cities" => PublicAddressKind.City, "coffee-zones" => PublicAddressKind.CoffeeZone,
        "beans" => PublicAddressKind.CoffeeBean,
        "equipments" => PublicAddressKind.Equipment,
        "brew-methods" => PublicAddressKind.BrewMethod,
        "shop-tags" => PublicAddressKind.ShopTag,
        _ => throw new PublicAddressException("Unknown address kind.", 400)
    };
}
