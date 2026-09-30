using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public class GetPublicUserProfileHandler
{
    public async Task<Response<PublicUserProfileResponse>> Handle(
        GetPublicUserProfileCommand command,
        IUserRepository userRepository,
        IMapper mapper,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);

        // D-04: deleted and blocked profiles are not public. The message is identical to the unknown-id case so they are
        // indistinguishable. The signature is unchanged, so pre-generated GetPublicUserProfileCommandHandler1926808727
        // stays valid.
        if (user is null || user.IsSoftDelete || user.IsBlocked)
        {
            throw new NotFoundException("User not found");
        }

        var result = mapper.Map<PublicUserProfileResponse>(user);

        return Response<PublicUserProfileResponse>.Success(result);
    }
}