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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ModerationRoastersController(IMessageBus bus, IUserContext userContext) : ControllerBase
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
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
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
    public async Task<IActionResult> SubmitRoaster([FromBody] SubmitRoasterCommand command, CancellationToken ct)
    {
        // Anonymous submissions carry a null UserId.
        var commandWithUser = command with { UserId = userContext.GetUserId() };
        var response = await bus.InvokeAsync<Response<SubmitRoasterResponse>>(commandWithUser, ct);

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
}
