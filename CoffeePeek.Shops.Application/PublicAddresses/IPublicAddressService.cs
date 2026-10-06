using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public interface IPublicAddressService
{
    Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct);
    Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct);
    Task<PublicAddressMetadata[]> GetForReadAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct);
    Task<Guid[]> ResolveIdsAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct);
    Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct);
    Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct);
}
