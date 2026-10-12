using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Application.Abstractions;

public sealed record GooglePlaceLookupResult(
    ImportGoogleBusinessStatus Status,
    string? MapsUri,
    DateTimeOffset FetchedAtUtc);
