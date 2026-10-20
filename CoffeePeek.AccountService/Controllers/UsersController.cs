using CoffeePeek.Account.Application.Features.Auth.CheckUserExistsByEmail;
using CoffeePeek.Account.Application.Features.Auth.Email.ConfirmEmail;
using CoffeePeek.Account.Application.Features.Auth.Email.ResendEmailConfirmation;
using CoffeePeek.Account.Application.Features.Auth.Email.ResendEmailConfirmationByEmail;
using CoffeePeek.Account.Application.Features.Auth.Password.ChangePassword;
using CoffeePeek.Account.Application.Features.Auth.Password.ForgotPassword;
using CoffeePeek.Account.Application.Features.Auth.Password.ResetPassword;
using CoffeePeek.Account.Application.Features.Auth.RegisterUser;
using CoffeePeek.Account.Application.Features.User.DeleteUser;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateAbout;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateAvatar;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateEmail;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdatePhoneNumber;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateUsername;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.AccountService.Controllers.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("User")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class UsersController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Get a user's public profile by id (gateway-trusted callers only)
    /// </summary>
    [HttpGet("/internal/users/{id:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType<Response<PublicUserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id,
        [FromServices] Microsoft.Extensions.Options.IOptions<CoffeePeek.Shared.Auth.Options.GatewayAuthOptions> options)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[CoffeePeek.Shared.Auth.Constants.GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey))
            return Unauthorized();
        var command = new GetPublicUserProfileCommand(id);
        var response = await bus.InvokeAsync<Response<PublicUserProfileResponse>>(command);
        return Ok(response);
    }

    /// <summary>
    /// Check if user exists by email
    /// </summary>
    [HttpGet("exists")]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CheckUser([FromQuery] string email)
    {
        var request = new CheckUserExistsByEmailCommand(email);

        var response = await bus.InvokeAsync<Response<bool>>(request);

        return Ok(response);
    }

    /// <summary>
    /// Register new user
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateEntityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand command)
    {
        var response = await bus.InvokeAsync<CreateEntityResponse>(command);
        return Ok(response);
    }

    #region Me

    /// <summary>
    /// Get user profile by id or current user profile
    /// </summary>
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize]
    [ProducesResponseType<Response<UserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(
        [FromServices] IProfileAddressService addresses,
        [FromServices] Microsoft.Extensions.Options.IOptions<CoffeePeek.Account.Application.Common.Options.PublicAddressOptions> flags,
        CancellationToken ct)
    {
        var request = new GetProfileCommand(userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<UserProfileResponse>>(request);
        if (response is { IsSuccess: true, Data: not null } && flags.Value.Enabled)
        {
            var address = (await addresses.GetByIdsAsync([userContext.GetUserIdOrThrow()], ct)).SingleOrDefault();
            return Ok(Response<UserProfileResponse>.Success(response.Data with
                { Address = address is null ? null : PublicAddressDto.From(address) }, response.Message));
        }
        return Ok(response);
    }

    /// <summary>
    /// Update user profile about information
    /// </summary>
    [HttpPatch("me/about")]
    [Authorize]
    [ProducesResponseType<UpdateEntityResponse<string>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileAboutCommand request,
        CancellationToken cancellationToken)
    {
        var command = request with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<string>>(command, cancellationToken);

        return Accepted(response);
    }

    /// <summary>
    /// Update user profile email
    /// </summary>
    [HttpPatch("me/email")]
    [Authorize]
    [ProducesResponseType<UpdateEntityResponse<string>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileEmailCommand request,
        CancellationToken cancellationToken)
    {
        var command = request with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<string>>(command, cancellationToken);

        return Accepted(response);
    }

    /// <summary>
    /// Update user profile phone number
    /// </summary>
    [HttpPatch("me/phone-number")]
    [Authorize]
    [ProducesResponseType<UpdateEntityResponse<string>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfilePhoneNumberCommand request,
        CancellationToken cancellationToken)
    {
        var command = request with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<string>>(command, cancellationToken);

        return Accepted(response);
    }

    /// <summary>
    /// Update user avatar
    /// </summary>
    [HttpPatch("me/avatar")]
    [Authorize]
    [ProducesResponseType<UpdateEntityResponse<PhotoMetadata>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateUserAvatarCommand command, CancellationToken cancellationToken)
    {
        command = command with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<PhotoMetadata>>(command, cancellationToken);

        return Accepted(response);
    }

    /// <summary>
    /// Update user username
    /// </summary>
    [HttpPatch("me/username")]
    [Authorize]
    [ProducesResponseType<Response<PublicUsernameUpdateResponse>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateProfileUsernameCommand command,
        [FromServices] IProfileAddressService addresses,
        [FromServices] Microsoft.Extensions.Options.IOptions<CoffeePeek.Account.Application.Common.Options.PublicAddressOptions> flags,
        CancellationToken cancellationToken)
    {
        command = command with
        {
            UserId = userContext.GetUserIdOrThrow()
        };

        var response = await bus.InvokeAsync<UpdateEntityResponse<string>>(command, cancellationToken);
        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(response);
        var address = flags.Value.Enabled
            ? (await addresses.GetByIdsAsync([command.UserId], cancellationToken)).SingleOrDefault() : null;
        return Accepted(Response<PublicUsernameUpdateResponse>.Success(new(response.Data,
            address is null ? null : PublicAddressDto.From(address)), response.Message));
    }

    /// <summary>
    /// Request account deletion or resend its confirmation email
    /// </summary>
    [HttpDelete("me")]
    [Authorize]
    [ProducesResponseType<Response<AccountDeletionRequestResponse>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteUser(CancellationToken cancellationToken)
    {
        var command = new DeleteUserCommand(userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<AccountDeletionRequestResponse>>(command, cancellationToken);

        return response.IsSuccess
            ? Accepted(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status500InternalServerError, response);
    }

    /// <summary>
    /// Confirm account deletion from the browser confirmation form
    /// </summary>
    [HttpPost("me/deletion-confirmation")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType<Response<AccountDeletionRequestResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> ConfirmDeletion(
        [FromForm] ConfirmAccountDeletionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AccountDeletionRequestResponse>>(
            new ConfirmAccountDeletionCommand(request.Token, request.Reason, request.OtherReason),
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status500InternalServerError, response);
    }

    /// <summary>
    /// Get the current account deletion request
    /// </summary>
    [HttpGet("me/deletion-request")]
    [Authorize]
    [ProducesResponseType<Response<AccountDeletionRequestResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeletionRequest(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AccountDeletionRequestResponse>>(
            new GetCurrentAccountDeletionRequestQuery(userContext.GetUserIdOrThrow()),
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status500InternalServerError, response);
    }

    /// <summary>
    /// Cancel a pending account deletion request using its confirmation token
    /// </summary>
    [HttpDelete("me/deletion-request")]
    [AllowAnonymous]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelDeletionRequest(
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response>(
            new CancelAccountDeletionRequestCommand(token),
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status500InternalServerError, response);
    }

    /// <summary>
    /// Change password for the current user
    /// </summary>
    [HttpPut("me/password")]
    [Authorize]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var command = request with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<Response>(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Request a password reset email (public, no auth required)
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response>(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Reset password using a reset token (public, no auth required)
    /// </summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response>(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Resend email confirmation by email address (public, no auth required)
    /// </summary>
    [HttpPost("email-confirmation/resend")]
    [AllowAnonymous]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResendEmailConfirmByEmail([FromBody] ResendEmailConfirmationByEmailCommand command,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response>(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Resend email confirmation
    /// </summary>
    [HttpPost("me/email-confirmation")]
    [Authorize]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResendEmailConfirm()
    {
        var command = new ResendEmailConfirmationCommand(userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response>(command);
        
        return Ok(response);
    }

    /// <summary>
    /// Confirm email
    /// </summary>
    [HttpPut("me/email-confirmation")]
    [AllowAnonymous]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
    {
        var response = await bus.InvokeAsync<Response>(new ConfirmEmailCommand(token));
        return Ok(response);
    }

    #endregion
}
