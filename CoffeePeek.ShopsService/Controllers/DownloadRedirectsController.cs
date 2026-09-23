using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Stable public download redirects.</summary>
[ApiController]
[Route("download")]
[AllowAnonymous]
[Tags("App Downloads")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class DownloadRedirectsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("android")]
    public Task<IActionResult> Android(CancellationToken ct) => RedirectChannel("android", ct);

    [HttpGet("apk")]
    public Task<IActionResult> Apk(CancellationToken ct) => RedirectChannel("apk", ct);

    [HttpGet("ios")]
    public Task<IActionResult> Ios(CancellationToken ct) => RedirectChannel("ios", ct);

    [HttpGet("auto")]
    public IActionResult Auto()
    {
        SetRedirectCacheHeaders();

        var userAgent = Request.Headers.UserAgent.ToString();
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return Redirect(AppDownloadRoutes.AndroidUrl);

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            return Redirect(AppDownloadRoutes.IosUrl);

        return Redirect(AppDownloadRoutes.DownloadPageUrl);
    }

    private async Task<IActionResult> RedirectChannel(string channel, CancellationToken ct)
    {
        SetRedirectCacheHeaders();

        var response = await bus.InvokeAsync<Response<string>>(
            new GetAppDownloadRedirectQuery(
                channel,
                Request.Headers.UserAgent.ToString(),
                Request.Headers.Referer.ToString(),
                Request.Headers.TryGetValue("CF-IPCountry", out var country) ? country.FirstOrDefault() : null),
            ct);

        return response.IsSuccess
            ? Redirect(response.Data)
            : NotFound(response);
    }

    private void SetRedirectCacheHeaders()
    {
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
