using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;
using CoffeePeek.Moderation.Application.Features.Review.GetModerationReviewById;
using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;
using CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;
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
public class ModerationReviewsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType(typeof(Response<GetAllModerationReviewsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAllModerationReviews(
        [FromQuery] GetAllModerationReviewsQuery query,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<GetAllModerationReviewsResponse>>(query, cancellationToken);

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
    [ProducesResponseType<Response<ModerationReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModerationReviewById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationReviewDto>>(new GetModerationReviewByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(CreateEntityResponse), statusCode: StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendReviewToModeration(
        [FromBody] SendReviewToModerationCommand command, CancellationToken ct)
    {
        command = command with
        {
            UserId = userContext.GetUserIdOrThrow(),
            UserName = userContext.GetUsernameOrThrow()
        };

        var response = await bus.InvokeAsync<CreateEntityResponse>(command, ct);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPut("{reviewId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(Response<UpdateCoffeeShopReviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateReview(
        [FromBody] UpdateCoffeeShopReviewCommand command, Guid reviewId, CancellationToken ct)
    {
        command = command with { UserId = userContext.GetUserIdOrThrow(), ReviewId = reviewId };
        var response = await bus.InvokeAsync<Response<UpdateCoffeeShopReviewResponse>>(command, ct);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPut]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<UpdateEntityResponse<ModerationStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ChangeStatusModerationReview(
        [FromBody] ChangeStatusModerationReviewCommand command, CancellationToken ct)
    {
        var commandWithUser = command with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<ModerationStatus>>(commandWithUser, ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
