using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using CoffeePeek.Account.Application.Common.Interfaces;

namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public class GetProfileHandler
{
    public static async Task<Response<UserProfileResponse>> Handle(GetProfileCommand command, IUserRepository userRepository, IMapper mapper, IProfileSocialStore social, CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);

        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        var state = await social.GetStateAsync(user.Id, null, ct);
        var result = mapper.Map<UserProfileResponse>(user) with
        {
            AddedRoastersCount = user.Statistics.AddedRoastersCount,
            FollowersCount = state.FollowersCount
        };

        return Response<UserProfileResponse>.Success(result);
    }
}
