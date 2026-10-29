using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.ShopsService.Controllers.Owner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/owner/ownership-claims")]
[Authorize]
[Tags("Owner")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OwnerOwnershipClaimsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<OwnershipClaimDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromBody] SubmitOwnershipClaimRequest request, CancellationToken ct)
    {
        if (request.BusinessType is not { } businessType)
            return BadRequest(new ErrorResponse("businessType is required."));

        var response = await bus.InvokeAsync<Response<OwnershipClaimDto>>(new SubmitOwnershipClaimCommand(
            userContext.GetUserIdOrThrow(), businessType, request.BusinessSlug, request.Contact, request.Evidence), ct);
        return response.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = response.Data.Id }, response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpGet]
    [ProducesResponseType<Response<OwnershipClaimPage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct,
        [FromQuery] OwnershipClaimStatus? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await bus.InvokeAsync<Response<OwnershipClaimPage>>(new GetOwnershipClaimsQuery(
            userContext.GetUserIdOrThrow(), status, Page: page, PageSize: pageSize), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<OwnershipClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<OwnershipClaimDto>>(
            new GetOwnershipClaimByIdQuery(id, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
