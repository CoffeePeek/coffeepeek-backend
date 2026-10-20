using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public static class GetProfileHandler
{
    public static async Task<Response<UserProfileResponse>> Handle(GetProfileCommand command, IUserRepository userRepository,
        IOptions<MediaPublicUrlOptions> media, IProfileSocialStore social, CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct) ?? throw new NotFoundException("User not found");
        var state = await social.GetStateAsync(user.Id, null, ct);
        var avatarUrl = MediaStorageUrlBuilder.BuildPublicUrl(media.Value.PublicEndpoint, media.Value.AvatarBucketName,
            user.PhotoMetadata?.StorageKey);

        return Response<UserProfileResponse>.Success(new UserProfileResponse(user.Username.Value, user.Credentials.Email.Value,
            user.CreatedAtUtc, user.About, avatarUrl, user.Statistics.CheckInCount, user.Statistics.AddedShopsCount)
        {
            AddedRoastersCount = user.Statistics.AddedRoastersCount,
            FollowersCount = state.FollowersCount
        });
    }
}
