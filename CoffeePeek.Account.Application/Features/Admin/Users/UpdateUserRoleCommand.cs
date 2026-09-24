using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Admin.Users;

public record UpdateUserRoleCommand(
    [property: JsonIgnore] Guid TargetUserId,
    [Required, MaxLength(50)] string Role,
    [property: JsonIgnore] Guid ActorUserId);

public static class UpdateUserRoleHandler
{
    public static async Task<Response<Guid>> Handle(
        UpdateUserRoleCommand command,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IUnitOfWork unitOfWork,
        IRevokedUserStore revokedUserStore,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        // Self-targeting also covers "last admin": acting on another admin implies at least two exist.
        if (command.ActorUserId == command.TargetUserId)
            return Response<Guid>.Error(HttpStatusCode.BadRequest, "Admins cannot change the role of their own account.");

        var user = await userRepository.GetById(command.TargetUserId, ct);
        if (user is null)
            return Response<Guid>.Error(HttpStatusCode.NotFound, "User not found.");

        var role = await roleRepository.GetRoleAsync(command.Role);
        if (role is null)
            return Response<Guid>.Error(HttpStatusCode.BadRequest, $"Role '{command.Role}' not found.");

        user.ReplaceRoles(role);

        await userRepository.Update(user, ct);
        await unitOfWork.SaveChangesAsync(ct);
        // Old tokens carry the previous role claims.
        await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);

        return Response<Guid>.Success(user.Id);
    }
}
