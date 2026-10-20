using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public class GetPublicUserProfileHandler
{
    public async Task<Response<PublicUserProfileResponse>> Handle(
        GetPublicUserProfileCommand command,
        IUserRepository userRepository,
        IOptions<MediaPublicUrlOptions> media,
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
        var avatarUrl = MediaStorageUrlBuilder.BuildPublicUrl(media.Value.PublicEndpoint, media.Value.AvatarBucketName,
            user.PhotoMetadata?.StorageKey);

        return Response<PublicUserProfileResponse>.Success(new PublicUserProfileResponse(user.Username.Value,
            user.CreatedAtUtc, user.About, avatarUrl, user.Statistics.CheckInCount, user.Statistics.AddedShopsCount)
        {
            AuthorId = user.PublicAuthorId,
            AddedRoastersCount = user.Statistics.AddedRoastersCount,
            FollowersCount = state.FollowersCount,
            IsFollowing = state.IsFollowing
        });
    }
}
