using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.Password.ChangePassword;

public static class ChangePasswordHandler
{
    public static async Task<Response> Handle(
        ChangePasswordCommand request,
        IUserRepository userRepository,
        IPasswordHasherService passwordHasher,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new DomainException("Password must be at least 8 characters long");

        var user = await userRepository.GetById(request.UserId, ct)
                   ?? throw new NotFoundException("User not found.");

        if (!user.Credentials.HasPasswordAuth)
            throw new DomainException("Password login not available");

        if (!user.Credentials.ValidatePassword(request.CurrentPassword, passwordHasher))
            throw new DomainException("Current password is incorrect");

        var newHash = passwordHasher.HashPassword(request.NewPassword);
        user.ChangePassword(newHash);

        await userRepository.Update(user, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.PasswordChanged, ct);

        return Response.Success(new { message = "Password changed successfully." });
    }
}
