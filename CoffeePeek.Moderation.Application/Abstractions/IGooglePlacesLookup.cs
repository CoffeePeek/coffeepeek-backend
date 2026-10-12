namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IGooglePlacesLookup
{
    Task<GooglePlaceLookupResult> LookupAsync(
        string name,
        decimal latitude,
        decimal longitude,
        CancellationToken ct = default);
}
