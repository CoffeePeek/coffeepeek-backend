using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Infrastructure.Account;

public sealed class AccountProfileAddressLookup(PublicAddressHttpClient client, IAuthorVisibilityLookup visibility, IUserContext user) : IProfileAddressLookup
{
    public async Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct)
    {
        var author = await client.ResolveAsync("account-user-lookup", "users", slug, ct);
        if (author is not null && (await visibility.ExcludedUserIdsAsync(user.GetUserId(), ct)).Contains(author.EntityId)) return null;
        return author;
    }
    public Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids, CancellationToken ct) =>
        client.GetByIdsAsync("account-user-lookup", "users", ids, ct);
    public Task<PublicProfileSummaryDto[]> GetProfilesByIdsAsync(Guid[] ids, CancellationToken ct) =>
        client.GetUserProfilesByIdsAsync("account-user-lookup", ids, ct);
}
