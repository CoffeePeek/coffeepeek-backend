using CoffeePeek.Contract.Dtos.Profiles;

namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IUserBlockStore
{
    Task<AuthorBlockPageDto> ListAsync(Guid viewerId, int pageSize, string? cursor, CancellationToken ct);
    Task<AuthorBlockStateDto> SetAsync(Guid viewerId, Guid publicAuthorId, bool blocked, CancellationToken ct);
    Task<Guid[]> ExcludedUserIdsAsync(Guid viewerId, CancellationToken ct);
    Task<BlockedAuthorDto?> FindOutgoingAsync(Guid viewerId, Guid targetUserId, CancellationToken ct);
}
