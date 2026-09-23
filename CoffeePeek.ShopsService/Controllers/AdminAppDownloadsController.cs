using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin management for app download channels and Android releases.</summary>
[ApiController]
[Route("api/admin/v1/app-downloads")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminAppDownloadsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<AdminAppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminAppDownloadsDto>>(
            new GetAdminAppDownloadsQuery(), ct);

        return Ok(response);
    }

    [HttpPut("android/google-play")]
    [ProducesResponseType<Response<AdminAppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGooglePlay(
        [FromBody] UpdateStoreDownloadChannelRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminAppDownloadsDto>>(
            new UpdateGooglePlayDownloadCommand(userContext.GetUserIdOrThrow(), request.Url, request.Enabled),
            ct);

        return AppDistributionResult(response);
    }

    [HttpPut("android/apk")]
    [ProducesResponseType<Response<AdminAppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateApkChannel(
        [FromBody] UpdateApkChannelRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminAppDownloadsDto>>(
            new UpdateAndroidApkChannelCommand(userContext.GetUserIdOrThrow(), request.Enabled),
            ct);

        return AppDistributionResult(response);
    }

    [HttpPut("ios/app-store")]
    [ProducesResponseType<Response<AdminAppDownloadsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAppStore(
        [FromBody] UpdateStoreDownloadChannelRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminAppDownloadsDto>>(
            new UpdateAppStoreDownloadCommand(userContext.GetUserIdOrThrow(), request.Url, request.Enabled),
            ct);

        return AppDistributionResult(response);
    }

    [HttpGet("android/releases")]
    [ProducesResponseType<Response<AndroidReleaseDto[]>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAndroidReleases(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AndroidReleaseDto[]>>(
            new GetAndroidReleasesQuery(), ct);

        return Ok(response);
    }

    [HttpPost("android/releases")]
    [ProducesResponseType<Response<AndroidReleaseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAndroidRelease(
        [FromBody] CreateAndroidReleaseRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AndroidReleaseDto>>(
            new CreateAndroidReleaseCommand(
                userContext.GetUserIdOrThrow(),
                request.Version,
                request.VersionCode,
                request.FileUrl,
                request.FileName,
                request.FileSize,
                request.Sha256,
                request.ReleasedAt),
            ct);

        return AppDistributionResult(response);
    }

    [HttpGet("android/releases/{id:guid}")]
    [ProducesResponseType<Response<AndroidReleaseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAndroidRelease(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AndroidReleaseDto>>(
            new GetAndroidReleaseByIdQuery(id), ct);

        return AppDistributionResult(response);
    }

    [HttpPost("android/releases/{id:guid}/publish")]
    [ProducesResponseType<Response<AndroidReleaseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublishAndroidRelease(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AndroidReleaseDto>>(
            new PublishAndroidReleaseCommand(userContext.GetUserIdOrThrow(), id), ct);

        return AppDistributionResult(response);
    }

    private IActionResult AppDistributionResult<T>(Response<T> response)
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
}
