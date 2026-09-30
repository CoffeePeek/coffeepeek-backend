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

public record AdminDeleteUserCommand(Guid TargetUserId, Guid ActorUserId);

public static class AdminDeleteUserHandler
{
    public static async Task<Response<bool>> Handle(
        AdminDeleteUserCommand command,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        // Self-targeting also covers "last admin": acting on another admin implies at least two exist.
        if (command.ActorUserId == command.TargetUserId)
            return Response<bool>.Error(HttpStatusCode.BadRequest, "Admins cannot delete their own account.");

        var user = await userRepository.GetById(command.TargetUserId, ct);
        if (user is null)
            return Response<bool>.Error("User not found");

        user.SetSoftDelete();
        user.RevokeAllSessions();

        await userRepository.Update(user, ct);
        // Wolverine commits after the handler; finish network calls before SaveChanges acquires the address lock.
        await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.UserDeleted, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Response<bool>.Success(true);
    }
}
