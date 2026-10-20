using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Admin.Users;

public record BlockUserCommand(Guid TargetUserId, bool Blocked, Guid ActorUserId);

public static class BlockUserHandler
{
    public static async Task<Response<bool>> Handle(
        BlockUserCommand command,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        // Self-targeting also covers "last admin": acting on another admin implies at least two exist.
        if (command.ActorUserId == command.TargetUserId)
            return Response<bool>.Error(HttpStatusCode.BadRequest, "Admins cannot block or unblock their own account.");

        var user = await userRepository.GetById(command.TargetUserId, ct);
        if (user is null)
            return Response<bool>.Error(HttpStatusCode.NotFound, "User not found.");

        if (user.IsSoftDelete)
            return Response<bool>.Error(HttpStatusCode.BadRequest, "Deleted users cannot be blocked or unblocked.");

        user.SetBlocked(command.Blocked);

        await unitOfWork.SaveChangesAsync(ct);
        if (command.Blocked)
        {
            await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
            await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.UserBlocked, ct);
        }

        return Response<bool>.Success(true);
    }
}
