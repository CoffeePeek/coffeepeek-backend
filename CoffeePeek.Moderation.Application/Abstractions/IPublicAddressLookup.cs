using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IPublicAddressLookup
{
    Task<PublicAddressMetadata?> ResolveAsync(string kind, string slug, CancellationToken ct);
    Task<Guid[]> ResolveIdsAsync(string kind, string[] slugs, CancellationToken ct);
    Task<PublicAddressMetadata[]> GetByIdsAsync(string kind, Guid[] ids, CancellationToken ct);
}
