using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Shared.Auth;

namespace CoffeePeek.Moderation.Infrastructure.Shops;

public sealed class ShopsPublicAddressLookup(PublicAddressHttpClient client) : IPublicAddressLookup
{
    public Task<PublicAddressMetadata?> ResolveAsync(string kind, string slug, CancellationToken ct) =>
        client.ResolveAsync("shops-roaster-lookup", kind, slug, ct);
    public Task<Guid[]> ResolveIdsAsync(string kind, string[] slugs, CancellationToken ct) =>
        client.ResolveIdsAsync("shops-roaster-lookup", kind, slugs, ct);
    public Task<PublicAddressMetadata[]> GetByIdsAsync(string kind, Guid[] ids, CancellationToken ct) =>
        client.GetByIdsAsync("shops-roaster-lookup", kind, ids, ct);
}
