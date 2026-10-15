using CoffeePeek.Contract.Dtos.ProfileActivity;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/profiles/{authorSlug}/activity")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfilesActivityController(IProfileAddressLookup profiles, IProfileActivityQueries activity) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(Response<ProfileActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string authorSlug, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string timeZone = "UTC", CancellationToken ct = default)
    {
        var period = ProfileActivityMonth.Parse(year, month, timeZone);
        var author = await profiles.ResolveAsync(authorSlug, ct);
        if (author == null) return NotFound(new ErrorResponse("Profile not found."));
        return Ok(Response<ProfileActivityDto>.Success(await activity.GetAsync(author.EntityId, period, ct)));
    }
}
