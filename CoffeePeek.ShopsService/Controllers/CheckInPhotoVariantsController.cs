using System.Net;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Observability;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/check-ins")]
public sealed class CheckInPhotoVariantsController(ICheckInPhotoAccessQueries queries, MediaCheckInPhotoClient media,
    IUserContext user, IOptions<CheckInPhotoDeliveryOptions> options, TimeProvider clock) : ControllerBase
{
    public static readonly TimeSpan DecisionBudget = CheckInPhotoPolicyObservation.DecisionLifetime;

    [HttpGet("{id:guid}/photos/{photoId:guid}/variants/{source}/{recipe:int}/{purpose}.{format}")]
    [HttpHead("{id:guid}/photos/{photoId:guid}/variants/{source}/{recipe:int}/{purpose}.{format}")]
    [AllowAnonymous]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Download(Guid id, Guid photoId, string source, int recipe,
        string purpose, string format, CancellationToken ct)
    {
        var readerId = user.GetUserId();
        var reader = readerId.HasValue ? "authenticated" : "anonymous";
        using var measurement = PhotoDeliveryTelemetry.Start("variant_download", reader);
        var status = 500;
        try
        {
            var result = await DownloadAsync(id, photoId, source, recipe, purpose, format, readerId, ct);
            status = result is StatusCodeResult code ? code.StatusCode : Response.StatusCode;
            measurement.Complete(status is 200 or 304 ? "complete" : status == 404 ? "denied" : "unavailable");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { status = 499; measurement.Complete("cancelled"); throw; }
        catch (ServiceUnavailableException)
        { status = 503; measurement.Complete("unavailable"); throw; }
        finally
        {
            PhotoDeliveryTelemetry.Response(reader, status, status is 200 or 304
                && Response.Headers.CacheControl.ToString().Contains("must-revalidate", StringComparison.Ordinal),
                status is 200 or 304 && Response.GetTypedHeaders().CacheControl?.MaxAge > TimeSpan.Zero);
        }
    }

    private async Task<IActionResult> DownloadAsync(Guid id, Guid photoId, string source, int recipe,
        string purpose, string format, Guid? readerId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.Vary = "Authorization, Cookie";
        if (!readerId.HasValue && (Request.Headers.ContainsKey("Authorization")
            || Request.Headers.ContainsKey(GatewayHeaderConsts.XUserId))) return Unauthorized();
        var expires = readerId.HasValue ? AccessTokenExpiry.Read(User) : null;
        if ((readerId.HasValue && User.HasClaim(c => c.Type == AccessTokenExpiry.ClaimType) && expires is null)
            || expires <= clock.GetUtcNow()) return Unauthorized();
        if (!options.Value.ServeVariants || !CheckInPhotoVariantIdentity.IsAddress(source, recipe, purpose, format)) return NotFound();
        var requestObservation = CheckInPhotoPolicyObservation.Start(clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(DecisionBudget);
        try
        {
            var access = await queries.GetVariantAsync(id, photoId, readerId, budget.Token);
            if (access is null || access.SourceVersion != source) return NotFound();
            var observation = access.PolicyObservation ?? requestObservation;
            TimeSpan Remaining() => TimeSpan.FromTicks(Math.Min(observation.Remaining(clock).Ticks,
                requestObservation.Remaining(clock).Ticks));
            if (Remaining() <= TimeSpan.Zero) return StatusCode(503);
            var credentialLifetime = expires - clock.GetUtcNow();
            if (credentialLifetime <= TimeSpan.Zero) return Unauthorized();
            var operationLifetime = credentialLifetime is { } life
                ? TimeSpan.FromTicks(Math.Min(Remaining().Ticks, life.Ticks)) : Remaining();
            if (operationLifetime <= TimeSpan.Zero) return StatusCode(503);
            budget.CancelAfter(operationLifetime);
            var conditional = access.IsPubliclyVisible ? Request.Headers.IfNoneMatch.ToString() : null;
            if (conditional?.Length > 4096) return BadRequest();
            using var response = await media.DownloadVariantAsync(access.OwnerId, id, photoId, source, recipe, purpose,
                format, HttpMethods.IsHead(Request.Method), conditional, budget.Token);
            if (Remaining() <= TimeSpan.Zero) return StatusCode(503);
            if (expires <= clock.GetUtcNow()) return Unauthorized();
            var unchanged = response.StatusCode == HttpStatusCode.NotModified;
            if (response.StatusCode != HttpStatusCode.OK && !(unchanged && access.IsPubliclyVisible && !string.IsNullOrEmpty(conditional)))
                return StatusCode(response.StatusCode == HttpStatusCode.NotFound ? 404 : 503);
            if (response.Content.Headers.ContentType?.MediaType != "image/" + format
                || response.Content.Headers.ContentLength is not (> 0 and <= 8 * 1024 * 1024)
                || response.Headers.ETag is null) return StatusCode(503);
            var freshness = access.PolicyObservation is not null
                && (!readerId.HasValue || expires.HasValue)
                ? Math.Min(CheckInPhotoPolicyObservation.ResponseLifetime.TotalSeconds, Remaining().TotalSeconds) : 0;
            if (expires is { } expiry) freshness = Math.Min(freshness, (expiry - clock.GetUtcNow()).TotalSeconds);
            var maxAge = (int)Math.Max(0, Math.Floor(freshness));
            Response.Headers.CacheControl = access.IsPubliclyVisible
                ? $"private, max-age={maxAge}, must-revalidate" : "private, no-store";
            // New representation/validation time, paired with the remaining original grant, not a renewed grant.
            Response.GetTypedHeaders().Date = clock.GetUtcNow();
            Response.Headers.ETag = response.Headers.ETag.ToString();
            Response.ContentType = response.Content.Headers.ContentType.MediaType;
            Response.ContentLength = response.Content.Headers.ContentLength;
            if (unchanged) return StatusCode(304);
            if (!HttpMethods.IsHead(Request.Method))
            {
                await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
                await PhotoDeliveryTelemetry.CopyAsync(stream, Response.Body, "shops_stream", budget.Token);
            }
            return new EmptyResult();
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (Response.HasStarted) { HttpContext.Abort(); return new EmptyResult(); }
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Remove("ETag"); Response.ContentLength = null;
            return StatusCode(503);
        }
    }
}
