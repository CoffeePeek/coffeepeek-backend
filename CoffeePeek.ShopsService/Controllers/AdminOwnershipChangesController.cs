using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/ownership-changes")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminOwnershipChangesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<OwnershipChangePage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct,
        [FromQuery] BusinessType? businessType = null, [FromQuery] Guid? businessId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await bus.InvokeAsync<Response<OwnershipChangePage>>(
            new GetOwnershipChangesQuery(businessType, businessId, page, pageSize), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
