using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.DeleteReviewFromCoffeeShop;
using CoffeePeek.Shops.Application.Features.Review.GetReviewById;
using CoffeePeek.Shops.Application.Features.Review.SetReviewHelpful;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CoffeeShopReviewsController(IMessageBus bus, IUserContext userContext, PublicDiscoveryService discovery) : ControllerBase
{
    /// <summary>Submit complaint text about a published review.</summary>
    [HttpPost("{reviewId:guid}/reports")]
    [ProducesResponseType<Response<ReviewReportDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(Guid reviewId, [FromBody] SubmitReviewReportRequest request, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ReviewReportDto>>(
            new SubmitReviewReportCommand(reviewId, userContext.GetUserIdOrThrow(), request.Text), ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Get review by ID
    /// </summary>
    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(Response<PublicReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetReviewById(Guid reviewId, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetReviewByIdResponse>>(
            new GetReviewByIdQuery(reviewId), ct);

        if (response is not { IsSuccess: true, Data: not null }) return NotFound();
        return Ok(Response<PublicReviewDto>.Success((await discovery.ReviewsAsync([response.Data.Review], ct))[0]));
    }

    /// <summary>
    /// Delete review by ID
    /// </summary>
    /// <param name="reviewId"></param>
    /// <returns></returns>
    [HttpDelete("{reviewId:guid}")]
    [ProducesResponseType(typeof(Response), StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteReview(Guid reviewId)
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

/// <summary>Complaint text only; identity comes from authentication and the route.</summary>
public record SubmitReviewReportRequest
{
    /// <summary>Required complaint text, up to 2000 characters.</summary>
    [Required, MaxLength(2000)]
    public string Text { get; init; } = null!;
}
