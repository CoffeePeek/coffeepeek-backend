using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;
using DomainUser = CoffeePeek.Account.Domain.Entities.UserAggregate.User;

namespace CoffeePeek.Account.Application.Features.Admin.Users;

public static class GetAdminUsersHandler
{
    public static async Task<Response<GetAdminUsersResponse>> Handle(
        GetAdminUsersQuery query,
        IAdminUserQueryRepository repository,
        IOptions<MediaPublicUrlOptions> media,
        CancellationToken ct)
    {
        var (items, totalCount) = await repository.GetUsersAsync(
            query.Page, query.PageSize, query.Search, query.Role, ct);

        var users = items.Select(u => MapUser(u, media.Value)).ToArray();
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return Response<GetAdminUsersResponse>.Success(new GetAdminUsersResponse(
            users,
            totalCount,
            totalPages,
            query.Page,
            query.PageSize));
    }

    internal static AdminUserResponse MapUser(DomainUser user, MediaPublicUrlOptions media) => new(
        user.Id,
        user.Username.Value,
        user.Credentials.Email.Value,
        user.CreatedAtUtc,
        user.About,
        MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.AvatarBucketName, user.PhotoMetadata?.StorageKey),
        user.Statistics.CheckInCount,
        user.Statistics.AddedShopsCount,
        user.Roles.Select(r => r.Name).ToArray(),
        user.IsBlocked || user.IsSoftDelete);
}
