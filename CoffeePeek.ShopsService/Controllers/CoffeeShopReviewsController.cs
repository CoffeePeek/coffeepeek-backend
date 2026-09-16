using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.DeleteReviewFromCoffeeShop;
using CoffeePeek.Shops.Application.Features.Review.GetReviewById;
using CoffeePeek.Shops.Application.Features.Review.SetReviewHelpful;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class CoffeeShopReviewsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Get review by ID
    /// </summary>
    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(Response<GetReviewByIdResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetReviewById(Guid reviewId)
    {
        var response = await bus.InvokeAsync<Response<GetReviewByIdResponse>>(
            new GetReviewByIdQuery(reviewId));

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>
    /// Delete review by ID
    /// </summary>
    /// <param name="shopId"></param>
    /// <param name="reviewId"></param>
    /// <returns></returns>
    [HttpDelete("{reviewId:guid}")]
    [ProducesResponseType(typeof(Response), StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteReview(Guid shopId, Guid reviewId)
    {
        var command = new DeleteReviewFromCoffeeShopCommand(reviewId, userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response>(command);

        return response.IsSuccess ? NoContent() : NotFound(response);
    }

    /// <summary>
    /// Mark a review as helpful (idempotent). Authors cannot vote on their own reviews.
    /// </summary>
    [HttpPut("{reviewId:guid}/helpful")]
    [ProducesResponseType(typeof(Response<SetReviewHelpfulResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MarkHelpful(Guid reviewId)
    {
        var command = new SetReviewHelpfulCommand(reviewId, true, userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<SetReviewHelpfulResponse>>(command);

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>
    /// Remove the current user's helpful vote from a review (idempotent).
    /// </summary>
    [HttpDelete("{reviewId:guid}/helpful")]
    [ProducesResponseType(typeof(Response<SetReviewHelpfulResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnmarkHelpful(Guid reviewId)
    {
        var command = new SetReviewHelpfulCommand(reviewId, false, userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<SetReviewHelpfulResponse>>(command);

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}