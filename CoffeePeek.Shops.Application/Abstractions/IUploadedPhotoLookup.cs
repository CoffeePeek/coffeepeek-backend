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
public record UploadedPhotoLookupResult(bool Succeeded, IReadOnlyList<ResolvedUploadedPhotoDto> Photos)
{
    public static UploadedPhotoLookupResult Failed { get; } = new(false, []);
}
