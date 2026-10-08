using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class ShopPrimaryEquipmentController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPut("admin/shops/{id:guid}/primary-equipment")]
    [Authorize(Policy = RoleConsts.Admin)]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetForAdmin(Guid id, SetShopPrimaryEquipmentRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new SetShopPrimaryEquipmentCommand(id, null, request.PrimaryEquipmentIds), ct));

    [HttpPut("owner/coffee-shops/{id:guid}/primary-equipment")]
    [Authorize(Policy = RoleConsts.Owner)]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetForOwner(Guid id, SetShopPrimaryEquipmentRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new SetShopPrimaryEquipmentCommand(id, userContext.GetUserIdOrThrow(), request.PrimaryEquipmentIds), ct));
}
