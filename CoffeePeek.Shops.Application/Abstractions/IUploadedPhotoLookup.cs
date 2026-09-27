using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Shops.Application.Abstractions;

/// <summary>
/// Resolves storage keys to trusted metadata for photos the user uploaded through MediaService
/// and has not attached anywhere yet.
/// </summary>
public interface IUploadedPhotoLookup
{
    Task<UploadedPhotoLookupResult> ResolveAsync(
        Guid userId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default);
}

/// <param name="Succeeded">False when MediaService could not be reached or rejected the request.</param>
/// <param name="Photos">Only keys that are owned by the user, pending, unexpired and actually uploaded.</param>
/// <param name="IsTransientFailure">True when Media was unreachable or returned 5xx — not a client photo error.</param>
public record UploadedPhotoLookupResult(
    bool Succeeded,
    IReadOnlyList<ResolvedUploadedPhotoDto> Photos,
    bool IsTransientFailure = false)
{
    public static UploadedPhotoLookupResult Failed { get; } = new(false, []);
    public static UploadedPhotoLookupResult Unreachable { get; } = new(false, [], true);
}
