using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.Abstractions;

// Account owns profiles; Shops never reads the Account database.
public interface IProfileAddressLookup
{
    Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct);
    Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids, CancellationToken ct);
}
