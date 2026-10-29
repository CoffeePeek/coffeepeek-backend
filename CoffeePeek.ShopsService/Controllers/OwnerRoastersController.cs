using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/owner/roasters")]
[Authorize]
[Tags("Owner")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class OwnerRoastersController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<OwnerRoastersPage>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyRoasters(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await bus.InvokeAsync<Response<OwnerRoastersPage>>(
            new GetOwnerRoastersQuery(userContext.GetUserIdOrThrow(), page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<RoasterDetailsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyRoaster(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<RoasterDetailsDto>>(
            new GetRoasterByIdQuery(id, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status404NotFound, response);
    }

    /// <summary>Save the profile using its current revision. Null photos retain the gallery; an empty list clears it.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<Response<RoasterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateMyRoaster(Guid id, [FromBody] UpdateRoasterRequest request,
        CancellationToken ct, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (ifMatch is null)
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                Response<RoasterDto>.Error((HttpStatusCode)StatusCodes.Status428PreconditionRequired, "If-Match is required."));
        if (!ShopProfileRevisionHeader.TryParse(ifMatch, out var revision))
            return BadRequest(Response<RoasterDto>.Error(HttpStatusCode.BadRequest,
                "If-Match must contain a nonnegative profile revision."));

        var owner = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<RoasterDto>>(request.ToCommand(id, owner, revision, owner), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
