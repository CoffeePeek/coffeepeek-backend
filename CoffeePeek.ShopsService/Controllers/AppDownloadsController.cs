using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public app download configuration for CoffeePeek clients.</summary>
[ApiController]
[Route("api/v1/app-downloads")]
[AllowAnonymous]
[Tags("App Downloads")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AppDownloadsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<Response<AppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AppDownloadsDto>>(new GetPublicAppDownloadsQuery(), ct);
        return Ok(response);
    }

    /// <summary>Latest and minimum supported build for <paramref name="platform"/> (android|ios). Builds below minVersionCode must update.</summary>
    /// <param name="platform">android | ios</param>
    /// <param name="channel">Android only: play | apk (the install source). Omitted = Play when available, else APK.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("~/api/v1/app-version")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<Response<AppVersionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(
        [FromQuery] string? platform,
        [FromQuery] string? channel,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AppVersionDto>>(new GetAppVersionQuery(platform, channel), ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
