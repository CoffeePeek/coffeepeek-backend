using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Roaster.GetAllModerationRoasters;
using CoffeePeek.Moderation.Application.Features.Roaster.GetModerationRoasterById;
using CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;
using CoffeePeek.Moderation.Application.Features.Roaster.UpdateModerationRoasterStatus;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Moderation.Application.Services;
using Mapster;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ModerationRoastersController(IMessageBus bus, IUserContext userContext, PublicModerationService discovery) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RoleConsts.Moderator)]
    [Description("Get roaster submissions for moderation with pagination and filters")]
    [ProducesResponseType<Response<GetAllModerationRoastersResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllModerationRoasters(
        [FromQuery] GetAllModerationRoastersQuery request, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllModerationRoastersResponse>>(request, ct);

        if (response.IsSuccess && response.Data is not null)
            AddPagingHeaders(response.Data.TotalItems, response.Data.TotalPages, response.Data.CurrentPage, response.Data.PageSize);

        return Ok(response);
    }

    /// <summary>
    /// Lists the caller's own roaster submissions, newest first. The submitter filter comes only from the
    /// authenticated user; the query string cannot select another user's items.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    [Description("Get the caller's own roaster submissions with moderation status")]
    [ProducesResponseType<Response<PublicPage<PublicModerationRoasterDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] ModerationStatus? status = null,
        CancellationToken ct = default)
    {
        var query = new GetAllModerationRoastersQuery(page, pageSize, status, UserId: userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<GetAllModerationRoastersResponse>>(query, ct);

        if (response.IsSuccess && response.Data is not null)
            AddPagingHeaders(response.Data.TotalItems, response.Data.TotalPages, response.Data.CurrentPage, response.Data.PageSize);

        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(response);
        var data = response.Data;
        return Ok(Response<PublicPage<PublicModerationRoasterDto>>.Success(new(await discovery.RoastersAsync(data.Items, ct),
            data.TotalItems, data.TotalPages, data.CurrentPage, data.PageSize)));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ModerationRoasterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModerationRoasterById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationRoasterDto>>(new GetModerationRoasterByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPost]
    [AllowAnonymous]
    [Description("Submits a new roaster to moderation")]
    [ProducesResponseType<Response<SubmitRoasterResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitRoaster([FromBody] PublicSubmitRoasterRequest request, CancellationToken ct)
    {
        // Anonymous submissions carry a null UserId.
        var commandWithUser = request.Adapt<SubmitRoasterCommand>() with
        { UserId = userContext.GetUserId(), CityId = request.City is null ? null : await WriteStageTrace.MeasureAsync("roaster_submit", "city_lookup",
            () => discovery.ResolveIdAsync("cities", request.City, ct)) };
        var response = await WriteStageTrace.MeasureAsync("roaster_submit", "commit",
            () => bus.InvokeAsync<Response<SubmitRoasterResponse>>(commandWithUser, ct));

        if (!response.IsSuccess)
        {
            return response.StatusCode switch
            {
                StatusCodes.Status409Conflict => Conflict(response),
                _ => BadRequest(response)
            };
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPut("status")]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> UpdateModerationRoasterStatus(
        [FromQuery, Required] Guid id,
        [FromQuery, Required] ModerationStatus status,
        [FromQuery] string? comment,
        CancellationToken ct)
    {
        var request = new UpdateModerationRoasterStatusCommand(
            userContext.GetUserIdOrThrow(), id, status, comment);

        var response = await bus.InvokeAsync<Response>(request, ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    private void AddPagingHeaders(int totalItems, int totalPages, int currentPage, int pageSize)
    {
        Response.Headers.TryAdd("X-Total-Count", totalItems.ToString());
        Response.Headers.TryAdd("X-Total-Pages", totalPages.ToString());
        Response.Headers.TryAdd("X-Current-Page", currentPage.ToString());
        Response.Headers.TryAdd("X-Page-Size", pageSize.ToString());
    }
}
