using System.Text.Json;
using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Import.ApplyImportDecisions;
using CoffeePeek.Moderation.Application.Features.Import.DecideImportCandidate;
using CoffeePeek.Moderation.Application.Features.Import.GetImportCandidateById;
using CoffeePeek.Moderation.Application.Features.Import.GetImportCandidates;
using CoffeePeek.Moderation.Application.Features.Import.GetImportDossierHints;
using CoffeePeek.Moderation.Application.Features.Import.GetImportStats;
using CoffeePeek.Moderation.Application.Features.Import.Menu;
using CoffeePeek.Moderation.Application.Features.Import.RefreshCoffeeMapImport;
using CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;
using CoffeePeek.Moderation.Application.Features.Import.DecideImportDuplicate;
using CoffeePeek.Moderation.Application.Features.Import.GetImportDuplicates;
using CoffeePeek.Moderation.Application.Features.Import.SuggestImportDuplicates;
using CoffeePeek.Moderation.Application.Features.Import.PatchImportContacts;
using CoffeePeek.Moderation.Application.Features.Import.RefreshGoogleStatus;
using CoffeePeek.Moderation.Application.Features.Import.RefreshOsmImport;
using CoffeeMapVerificationSignals = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.CoffeeMapVerificationSignals;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/admin/import")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin Import")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminImportController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost("osm/refresh")]
    [ProducesResponseType<Response<OsmRefreshResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshOsm(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<OsmRefreshResultDto>>(new RefreshOsmImportCommand(), ct);
        if (response.IsSuccess)
            return Ok(response);

        return StatusCode(response.StatusCode ?? StatusCodes.Status504GatewayTimeout, response);
    }

    [HttpPost("coffeemap/refresh")]
    [ProducesResponseType<Response<CoffeeMapRefreshResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshCoffeeMap(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<CoffeeMapRefreshResultDto>>(
            new RefreshCoffeeMapImportCommand(), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? StatusCodes.Status500InternalServerError, response);
    }

    [HttpPost("decisions")]
    [ProducesResponseType<Response<ApplyImportDecisionsResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyDecisions(
        [FromBody] JsonElement body,
        [FromQuery] bool overrideClosed = false,
        CancellationToken ct = default)
    {
        Dictionary<string, string>? decisions = null;
        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty("decisions", out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            decisions = nested.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        var command = new ApplyImportDecisionsCommand(
            decisions,
            body,
            overrideClosed,
            userContext.GetUserIdOrThrow());

        var response = await bus.InvokeAsync<Response<ApplyImportDecisionsResultDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("file")]
    [RequestSizeLimit(10 * 1024 * 1024)] // matches the gateway Kestrel body limit
    [ProducesResponseType<Response<IngestImportFileResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IngestFile([FromBody] JsonElement body, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<IngestImportFileResultDto>>(
            new IngestImportFileCommand(body), ct);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("candidates")]
    [ProducesResponseType<Response<GetImportCandidatesResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCandidates(
        [FromQuery] ImportQueueStatus? status = null,
        [FromQuery] ImportCollectorBucket? bucket = null,
        [FromQuery] CoffeeShopType? type = null,
        [FromQuery] ImportRejectReason? rejectReason = null,
        [FromQuery] string? search = null,
        [FromQuery] string? name = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ImportSource? source = null,
        CancellationToken ct = default)
        => await GetCandidatesCore(status, bucket, type, rejectReason, search, name, page, pageSize, source, null, ct);

    // A separate versioned route keeps the existing candidate-list contract unchanged.
    [HttpGet("v2/candidates/recheck")]
    [ProducesResponseType<Response<GetImportCandidatesResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecheckCandidates(
        [FromQuery] ImportQueueStatus? status = null,
        [FromQuery] ImportCollectorBucket? bucket = null,
        [FromQuery] CoffeeShopType? type = null,
        [FromQuery] ImportRejectReason? rejectReason = null,
        [FromQuery] string? search = null,
        [FromQuery] string? name = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ImportSource? source = null,
        CancellationToken ct = default)
        => await GetCandidatesCore(status, bucket, type, rejectReason, search, name, page, pageSize, source,
            CoffeeMapVerificationSignals.NeedsRecheck, ct);

    private async Task<IActionResult> GetCandidatesCore(
        ImportQueueStatus? status,
        ImportCollectorBucket? bucket,
        CoffeeShopType? type,
        ImportRejectReason? rejectReason,
        string? search,
        string? name,
        int page,
        int pageSize,
        ImportSource? source,
        string? requiredSignal,
        CancellationToken ct)
    {
        var term = string.IsNullOrWhiteSpace(search) ? name : search;
        var response = await bus.InvokeAsync<Response<GetImportCandidatesResponse>>(
            new GetImportCandidatesQuery(status ?? ImportQueueStatus.Pending, bucket, type, rejectReason, term, page, pageSize, source, requiredSignal), ct);

        if (response.IsSuccess && response.Data is not null)
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
    }

    [HttpGet("candidates/{id:guid}")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCandidate(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new GetImportCandidateByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPatch("candidates/{id:guid}")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchContacts(
        Guid id,
        [FromBody] PatchImportContactsRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new PatchImportContactsCommand(
                id,
                request.Instagram,
                request.Phone,
                request.Website,
                request.OpeningHours),
            ct);

        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            StatusCodes.Status400BadRequest => BadRequest(response),
            _ => StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response)
        };
    }

    [HttpPost("candidates/{id:guid}/google-refresh")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshGoogle(
        Guid id,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new RefreshGoogleStatusCommand(id, force), ct);

        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            StatusCodes.Status400BadRequest => BadRequest(response),
            _ => StatusCode(response.StatusCode ?? StatusCodes.Status502BadGateway, response)
        };
    }

    [HttpPost("candidates/{id:guid}/decide")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decide(
        Guid id,
        [FromBody] DecideImportCandidateRequest request,
        CancellationToken ct)
    {
        var command = new DecideImportCandidateCommand(
            id,
            request.Status,
            request.Type,
            request.TagSlugs,
            request.OverrideClosed,
            userContext.GetUserIdOrThrow(),
            request.RejectReason);

        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }

    [HttpPost("candidates/{id:guid}/menu/photos")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachMenuPhotos(
        Guid id,
        [FromBody] AttachMenuPhotosRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new AttachImportCandidateMenuPhotosCommand(id, request.Photos, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    [HttpPost("candidates/{id:guid}/menu/parse")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ParseMenu(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new ParseImportCandidateMenuCommand(id, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    [HttpPut("candidates/{id:guid}/menu")]
    [ProducesResponseType<Response<ShopImportCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMenu(
        Guid id,
        [FromBody] UpdateShopMenuRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopImportCandidateDto>>(
            new UpdateImportCandidateMenuCommand(
                id, request.Items, request.ApplySuggestedPriceRange, userContext.GetUserIdOrThrow()),
            ct);
        return MenuActionResult(response);
    }

    private IActionResult MenuActionResult(Response<ShopImportCandidateDto> response)
    {
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            StatusCodes.Status400BadRequest => BadRequest(response),
            _ => StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response)
        };
    }

    [HttpGet("hints")]
    [ProducesResponseType<Response<ImportDossierHintsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHints(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ImportDossierHintsDto>>(
            new GetImportDossierHintsQuery(), ct);
        return Ok(response);
    }

    [HttpGet("stats")]
    [ProducesResponseType<Response<ImportStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ImportStatsDto>>(new GetImportStatsQuery(), ct);
        return Ok(response);
    }

    [HttpPost("duplicates/refresh")]
    [ProducesResponseType<Response<RefreshImportDuplicatesResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshDuplicates(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<RefreshImportDuplicatesResultDto>>(
            new SuggestImportDuplicatesCommand(), ct);
        return Ok(response);
    }

    [HttpGet("duplicates")]
    [ProducesResponseType<Response<GetImportDuplicatesResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDuplicates(
        [FromQuery] ImportDuplicateStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var response = await bus.InvokeAsync<Response<GetImportDuplicatesResponse>>(
            new GetImportDuplicatesQuery(status ?? ImportDuplicateStatus.Pending, page, pageSize), ct);

        if (response.IsSuccess && response.Data is not null)
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
    }

    [HttpPost("duplicates/{id:guid}/decide")]
    [ProducesResponseType<Response<DecideImportDuplicateResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DecideDuplicate(
        Guid id,
        [FromBody] DecideImportDuplicateRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<DecideImportDuplicateResultDto>>(
            new DecideImportDuplicateCommand(id, request.Accept, userContext.GetUserIdOrThrow()), ct);

        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }
}

public record DecideImportCandidateRequest(
    ImportQueueStatus Status,
    CoffeeShopType? Type,
    string[]? TagSlugs,
    bool OverrideClosed = false,
    ImportRejectReason? RejectReason = null);

public record DecideImportDuplicateRequest(bool Accept);

/// <summary>
/// Null / omitted field is left unchanged. Empty string clears the field.
/// </summary>
public record PatchImportContactsRequest(
    string? Instagram = null,
    string? Phone = null,
    string? Website = null,
    string? OpeningHours = null);
