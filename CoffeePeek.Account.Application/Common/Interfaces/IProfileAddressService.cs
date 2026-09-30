using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Account.Application.Common.Interfaces;

/// <summary>
/// Read-only lookup of user profile addresses plus the bounded admin backfill. There is no actor parameter:
/// Account keeps no address audit (D-06), and there is no admin initialize, rename or history for users (D-10).
/// Every method throws <c>ServiceUnavailableException</c> (503) while public addresses are disabled.
/// </summary>
public interface IProfileAddressService
{
    /// <summary>
    /// Resolves a canonical or alias slug (case-insensitive) to the owner's current canonical address.
    /// Returns null when the slug is unknown or retired, or when the owner is soft-deleted or blocked (D-03).
    /// Throws <c>ValidationException</c> (400) for an invalid, reserved or GUID-shaped slug.
    /// </summary>
    Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Returns the canonical addresses of the visible users among 1–100 non-empty ids, in input order.
    /// Unknown, soft-deleted and blocked users are omitted. Throws <c>ValidationException</c> (400) otherwise.
    /// </summary>
    Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids, CancellationToken ct);

    /// <summary>
    /// Allocates missing canonical addresses for non-deleted users (blocked users included, allocated but hidden),
    /// paged by user id after <paramref name="afterId"/>. A dry run writes nothing; a replay never renames and reports
    /// <c>AlreadyInitialized</c> (D-10). Throws <c>ValidationException</c> (400) unless the limit is 1–100.
    /// </summary>
    Task<PublicAddressBackfillResult> BackfillAsync(Guid? afterId, int limit, bool dryRun, CancellationToken ct);
}
