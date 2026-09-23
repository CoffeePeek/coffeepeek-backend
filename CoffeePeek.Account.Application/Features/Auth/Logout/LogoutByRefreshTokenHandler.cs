using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Application.Features.Auth.Logout;

public static class LogoutByRefreshTokenHandler
{
    public static async Task Handle(
        LogoutByRefreshTokenCommand request,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetByRefreshToken(request.RefreshToken, ct)
                   ?? throw new UnauthorizedException("Invalid refresh token");

        user.Logout(request.RefreshToken);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
