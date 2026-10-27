using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Contract.Dtos.Leaderboards;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/leaderboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LeaderboardsController(ILeaderboardStore store, IUserContext user, TimeProvider time) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<LeaderboardPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct, [FromQuery] string period = "week",
        [FromQuery] DateOnly? date = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(Response<LeaderboardPageDto>.Success(await store.GetAsync(
            LeaderboardPeriod.Create(period, date, time.GetUtcNow()), page, pageSize, user.GetUserId(), ct)));
}
