using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;

public sealed record ParsedImportPlace(
    OsmCandidateSnapshot Snapshot,
    ImportSource Source,
    string? GoogleMapsUri);
