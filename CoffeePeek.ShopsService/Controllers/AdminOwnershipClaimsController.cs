using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.ShopsService.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/ownership-claims")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminOwnershipClaimsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<OwnershipClaimPage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct,
        [FromQuery] OwnershipClaimStatus? status = null, [FromQuery] BusinessType? businessType = null,
        [FromQuery] Guid? businessId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await bus.InvokeAsync<Response<OwnershipClaimPage>>(new GetOwnershipClaimsQuery(
            null, status ?? OwnershipClaimStatus.Pending, businessType, businessId, page, pageSize), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<OwnershipClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<OwnershipClaimDto>>(new GetOwnershipClaimByIdQuery(id, null), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<Response<OwnershipClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Approve(Guid id, [FromBody] ReviewOwnershipClaimRequest request, CancellationToken ct) =>
        Review(id, request.Reason, true, ct);

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<Response<OwnershipClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reject(Guid id, [FromBody] ReviewOwnershipClaimRequest request, CancellationToken ct) =>
        Review(id, request.Reason, false, ct);

    private async Task<IActionResult> Review(Guid id, string? reason, bool approve, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<OwnershipClaimDto>>(
            new ReviewOwnershipClaimCommand(id, userContext.GetUserIdOrThrow(), approve, reason), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
