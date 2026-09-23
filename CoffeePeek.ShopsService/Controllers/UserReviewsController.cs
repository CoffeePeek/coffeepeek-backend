using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.GetReviewsByUserId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/users/{userId:guid}/reviews")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class UserReviewsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Get reviews for user
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<GetReviewsByUserIdResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserReviews(
        Guid userId,
        [FromQuery] int pageNumber = 1,
        [FromQuery][Range(1, 100)] int pageSize = 10)
    {
        if (userId == Guid.Empty)
            return BadRequest(Response<GetReviewsByUserIdResponse>.Error("Invalid user ID"));

        pageNumber = Math.Clamp(pageNumber, 1, 10_000); // larger values overflow Skip((page-1)*size)
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = new GetReviewsByUserIdQuery(userId, pageNumber, pageSize);
        var response = await bus.InvokeAsync<Response<GetReviewsByUserIdResponse>>(query);

        if (response is { IsSuccess: true, Data: not null })
        {
            Response.Headers["X-Total-Count"] = response.Data.TotalItems.ToString();
            Response.Headers["X-Total-Pages"] = response.Data.TotalPages.ToString();
            Response.Headers["X-Current-Page"] = response.Data.CurrentPage.ToString();
            Response.Headers["X-Page-Size"] = response.Data.PageSize.ToString();
        }

        return Ok(response);
    }
}