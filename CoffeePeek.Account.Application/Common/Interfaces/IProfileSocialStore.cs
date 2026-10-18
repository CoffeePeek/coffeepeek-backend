using CoffeePeek.Contract.Dtos.Profiles;

namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IProfileSocialStore
{
    Task<ProfileFollowStateDto> GetStateAsync(Guid userId, Guid? viewerId, CancellationToken ct);
    Task<ProfileFollowPageDto> ListAsync(Guid userId, bool following, int page, int pageSize, CancellationToken ct, Guid? viewerId = null);
    Task SetAsync(Guid followerId, Guid followedId, bool following, CancellationToken ct);
}
