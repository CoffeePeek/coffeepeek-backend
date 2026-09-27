using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public enum PublicAddressKind { CoffeeShop = 1, Roaster = 2 }

public sealed class PublicAddressOptions
{
    // Enable only after migrations and the live PostgreSQL transaction gate.
    public bool Enabled { get; set; }
}

public sealed class PublicAddressException(string message, int statusCode)
    : BaseException(message, "public_address_error", statusCode);

public interface IPublicAddressService
{
    Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct);
    Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct);
    Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct);
    Task<PublicAddressHistoryItem[]> HistoryAsync(PublicAddressKind kind, Guid id, long afterRevision, int limit, CancellationToken ct);
    Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct);
}
