using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Moderation.Application.Abstractions;

/// <summary>
/// Resolves storage keys to trusted metadata for photos the user uploaded through MediaService
/// and has not attached anywhere yet.
/// </summary>
public interface IReviewPhotoLookup
{
    Task<ReviewPhotoLookupResult> ResolveAsync(
        Guid userId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default);
}

/// <param name="Succeeded">False when MediaService could not be reached or rejected the request.</param>
/// <param name="Photos">Only keys that are owned by the user, pending, unexpired and actually uploaded.</param>
/// <param name="IsTransientFailure">True when Media was unreachable or returned 5xx — not a client photo error.</param>
public record ReviewPhotoLookupResult(
    bool Succeeded,
    IReadOnlyList<ResolvedUploadedPhotoDto> Photos,
    bool IsTransientFailure = false)
{
    public static ReviewPhotoLookupResult Failed { get; } = new(false, []);
    public static ReviewPhotoLookupResult Unreachable { get; } = new(false, [], true);
}
