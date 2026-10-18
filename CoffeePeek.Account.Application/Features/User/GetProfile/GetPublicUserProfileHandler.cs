using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using CoffeePeek.Account.Application.Common.Interfaces;

namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public class GetPublicUserProfileHandler
{
    public async Task<Response<PublicUserProfileResponse>> Handle(
        GetPublicUserProfileCommand command,
        IUserRepository userRepository,
        IMapper mapper,
        IProfileSocialStore social,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);

        // Deleted and blocked profiles return the same result as an unknown user.
        if (user is null || user.IsSoftDelete || user.IsBlocked)
        {
            throw new NotFoundException("User not found");
        }

        var state = await social.GetStateAsync(user.Id, command.ViewerId, ct);
        var result = mapper.Map<PublicUserProfileResponse>(user) with
        {
            AuthorId = user.PublicAuthorId,
            AddedRoastersCount = user.Statistics.AddedRoastersCount,
            FollowersCount = state.FollowersCount,
            IsFollowing = state.IsFollowing
        };

        return Response<PublicUserProfileResponse>.Success(result);
    }
}
