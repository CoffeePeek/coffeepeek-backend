using System.ComponentModel;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class PhotosController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Generate presigned upload url for avatar photo
    /// </summary>
    /// <param name="command"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("avatar")]
    [Description("Get url for presigned upload avatar photo")]
    [ProducesResponseType<Response<GenerateUploadUrlResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateUploadUrl([FromBody] GenerateAvatarUploadCommand command, CancellationToken ct)
    {
        command = command with { OwnerId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<Response<GenerateUploadUrlResponse>>(command, ct);
        return Ok(response);
    }
    
    /// <summary>
    /// Generate presigned upload urls for shop photos
    /// </summary>
    /// <param name="requests"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("shop")]
    [Description("Get urls for presigned upload photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>( StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var command = new GenerateShopPhotosCommand(requests, ownerId);
        var response = await bus.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Generate presigned upload urls for menu photos (shop bucket, prefix menus/).
    /// </summary>
    [HttpPost("menu")]
    [Description("Get urls for presigned upload menu photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateMenuUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var command = new GenerateShopPhotosCommand(requests, ownerId, "menus");
        var response = await bus.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Generate presigned upload urls for roaster photos (shop bucket, prefix roasters/).
    /// </summary>
    [HttpPost("roaster")]
    [Description("Get urls for presigned upload roaster photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateRoasterUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var command = new GenerateShopPhotosCommand(requests, ownerId, "roasters");
        var response = await bus.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(command, ct);
        return Ok(response);
    }
}