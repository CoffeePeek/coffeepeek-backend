using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Admin.Users.Sessions;

public record RevokeAllAdminUserSessionsCommand(Guid UserId);

public static class RevokeAllAdminUserSessionsHandler
{
    public static async Task<Response<bool>> Handle(
        RevokeAllAdminUserSessionsCommand command,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);
        if (user is null)
            return Response<bool>.Error(HttpStatusCode.NotFound, "User not found.");

        user.RevokeAllSessions();

        await unitOfWork.SaveChangesAsync(ct);
        await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.AllSessionsRevoked, ct);

        return Response<bool>.Success(true);
    }
}
