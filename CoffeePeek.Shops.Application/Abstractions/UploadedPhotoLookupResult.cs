using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Abstractions;

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
