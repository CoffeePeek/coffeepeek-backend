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
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("User")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class UsersController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Get user profile by id or current user profile
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<PublicUserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid? id)
    {
        var command = new GetPublicUserProfileCommand(id ?? userContext.GetUserIdOrThrow());
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
    [Authorize]
    [ProducesResponseType<Response<UserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById()
    {
        var request = new GetProfileCommand(userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<UserProfileResponse>>(request);
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
    [ProducesResponseType<UpdateEntityResponse<string>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateProfileUsernameCommand command,
        CancellationToken cancellationToken)
    {
        command = command with
        {
            UserId = userContext.GetUserIdOrThrow()
        };

        var response = await bus.InvokeAsync<UpdateEntityResponse<string>>(command, cancellationToken);

        return Accepted(response);
    }

    /// <summary>
    /// Request account deletion or resend its confirmation email
    /// </summary>
    [HttpDelete("me")]
    [Authorize]
    [ProducesResponseType<Response<AccountDeletionRequestResponse>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteUser(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestAccountDeletionRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new DeleteUserCommand(
            userContext.GetUserIdOrThrow(),
            request?.Reason ?? AccountDeletionReason.NoLongerUsing,
            request?.OtherReason);
        var response = await bus.InvokeAsync<Response<AccountDeletionRequestResponse>>(command, cancellationToken);

        return response.IsSuccess
            ? Accepted(response)
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