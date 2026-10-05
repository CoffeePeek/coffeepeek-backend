using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

// Values are persisted in PublicAddressAudit.Kind (CK_PublicAddressAudit_Kind); never renumber.
public enum PublicAddressKind { CoffeeShop = 1, Roaster = 2, City = 3, CoffeeZone = 4, CoffeeBean = 5, Equipment = 6, BrewMethod = 7, ShopTag = 8 }

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
    Task<PublicAddressMetadata[]> GetForReadAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct);
    Task<Guid[]> ResolveIdsAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct);
    Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct);
    Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct);
}
