using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Auth.Password.ForgotPassword;

public static class ForgotPasswordHandler
{
    private const string SilentMessage =
        "If an account with that email exists, a password reset link is on its way.";

    public static async Task<(Response, PasswordResetRequestedInternalEvent?)> Handle(
        ForgotPasswordCommand request,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetByEmail(request.Email, ct);

        // Silent success — do not reveal whether email exists or supports password auth
        if (user is null || !user.Credentials.HasPasswordAuth)
            return (Response.Success(new { message = SilentMessage }), null);

        user.BeginPasswordReset();
        await unitOfWork.SaveChangesAsync(ct);

        var @event = new PasswordResetRequestedInternalEvent(
            user.Id,
            user.Credentials.Email.Value,
            user.Username.Value,
            user.Credentials.PasswordResetToken!);

        return (Response.Success(new { message = SilentMessage }), @event);
    }
}
