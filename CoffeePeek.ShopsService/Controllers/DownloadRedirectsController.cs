using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.Shops.Application.Observability;
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
public class DownloadRedirectsController(IMessageBus bus, ILogger<DownloadRedirectsController> logger) : ControllerBase
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

        var userAgent = Request.Headers.UserAgent.ToString();
        var referer = Request.Headers.Referer.ToString();
        var country = Request.Headers.TryGetValue("CF-IPCountry", out var countryHeader)
            ? countryHeader.FirstOrDefault()
            : null;
        var response = await bus.InvokeAsync<Response<string>>(
            new GetAppDownloadRedirectQuery(channel), ct);

        if (!response.IsSuccess)
            return NotFound(response);

        var analytics = new RecordAppDownloadRedirectCommand(
            Guid.NewGuid(), channel, userAgent, referer, country);
        try
        {
            // Production Wolverine configuration persists local queues in PostgreSQL.
            await bus.PublishAsync(analytics);
        }
        catch (Exception queueException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(queueException,
                "Could not queue download redirect analytics {EventId} for {Channel}; trying a direct durable write",
                analytics.EventId, channel);
            try
            {
                // If queue persistence failed while PostgreSQL is still available, run the
                // idempotent handler inline. The same EventId is safe if the first attempt
                // actually queued but failed before acknowledging the enqueue.
                await bus.InvokeAsync(analytics, ct);
                ShopsTelemetry.RedirectAnalyticsFallbackUsed(channel);
            }
            catch (Exception writeException) when (!ct.IsCancellationRequested)
            {
                // This analytics sample is now unconfirmed. Keep the resolved redirect
                // available, and surface the loss risk to operations instead of implying
                // that a later retry is guaranteed.
                ShopsTelemetry.RedirectAnalyticsUnconfirmed(channel);
                logger.LogError(writeException,
                    "Download redirect analytics {EventId} for {Channel} could not be confirmed after queue and direct write failures",
                    analytics.EventId, channel);
            }
        }

        return Redirect(response.Data);
    }

    private void SetRedirectCacheHeaders()
    {
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
