using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Users.Sessions;

public record RevokeAdminUserSessionCommand(Guid UserId, Guid SessionId);

public static class RevokeAdminUserSessionHandler
{
    public static async Task<Response<bool>> Handle(
        RevokeAdminUserSessionCommand command,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ISessionTerminationNotifier sessionTerminationNotifier,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);
        if (user is null)
            return Response<bool>.Error(HttpStatusCode.NotFound, "User not found.");

        if (!user.RevokeSession(command.SessionId))
            return Response<bool>.Error(HttpStatusCode.NotFound, "Session not found.");

        await unitOfWork.SaveChangesAsync(ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.SessionRevoked, ct);

        return Response<bool>.Success(true);
    }
}
