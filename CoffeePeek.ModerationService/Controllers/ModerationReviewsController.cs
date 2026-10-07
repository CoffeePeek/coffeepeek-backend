using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;
using CoffeePeek.Moderation.Application.Features.Review.GetModerationReviewById;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/v1/moderation/check-ins")]
[Authorize(Policy = RoleConsts.Moderator)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ModerationReviewsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<GetAllModerationReviewsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllModerationReviews(
        [FromQuery] GetAllModerationReviewsQuery query, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllModerationReviewsResponse>>(query, ct);
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
    [ProducesResponseType<Response<ModerationReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModerationReviewById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationReviewDto>>(new GetModerationReviewByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPut]
    [ProducesResponseType<UpdateEntityResponse<ModerationStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatusModerationReview(
        [FromBody] ChangeStatusModerationReviewCommand command, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<UpdateEntityResponse<ModerationStatus>>(
            command with { UserId = userContext.GetUserIdOrThrow() }, ct);
        return response.IsSuccess ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
