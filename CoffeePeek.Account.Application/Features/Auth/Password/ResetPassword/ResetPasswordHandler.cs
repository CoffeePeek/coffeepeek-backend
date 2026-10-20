using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.Password.ResetPassword;

public static class ResetPasswordHandler
{
    public static async Task<Response> Handle(
        ResetPasswordCommand request,
        IUserRepository userRepository,
        IPasswordHasherService passwordHasher,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        UserCredential.EnsureValidNewPassword(request.NewPassword);

        var user = await userRepository.GetByPasswordResetToken(request.Token, ct)
                   ?? throw new NotFoundException("Invalid or expired reset token.");

        var newHash = passwordHasher.HashPassword(request.NewPassword);
        user.ResetPassword(request.Token, newHash);

        await unitOfWork.SaveChangesAsync(ct);
        await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.PasswordReset, ct);

        return Response.Success(new { message = "Password reset successfully." });
    }
}
