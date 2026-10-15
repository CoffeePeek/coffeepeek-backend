using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Infrastructure.Account;

public sealed class AccountProfileAddressLookup(PublicAddressHttpClient client) : IProfileAddressLookup
{
    public Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct) =>
        client.ResolveAsync("account-user-lookup", "users", slug, ct);
    public Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids, CancellationToken ct) =>
        client.GetByIdsAsync("account-user-lookup", "users", ids, ct);
    public Task<PublicProfileSummaryDto[]> GetProfilesByIdsAsync(Guid[] ids, CancellationToken ct) =>
        client.GetUserProfilesByIdsAsync("account-user-lookup", ids, ct);
}
