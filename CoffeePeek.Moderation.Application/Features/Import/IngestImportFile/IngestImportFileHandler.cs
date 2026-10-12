using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Application.Features.Import.SuggestImportDuplicates;
using CoffeePeek.Shared.Domain.Places;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;

public static class IngestImportFileHandler
{
    public static async Task<(Response<IngestImportFileResultDto>, object?)> Handle(
        IngestImportFileCommand command,
        IShopImportCandidateRepository repository,
        IShopImportDuplicateSuggestionRepository suggestions,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (command.Body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return (Response<IngestImportFileResultDto>.Error(
                HttpStatusCode.BadRequest,
                "JSON body is required."), null);
        }

        if (ImportFileParser.LooksLikeDecisionsFile(command.Body))
        {
            return (Response<IngestImportFileResultDto>.Error(
                HttpStatusCode.BadRequest,
                "This looks like import-decisions.json. POST /api/admin/import/decisions instead."), null);
        }

        var parsed = ImportFileParser.Parse(command.Body);
        if (parsed.Count == 0)
        {
            return (Response<IngestImportFileResultDto>.Error(
                HttpStatusCode.BadRequest,
                "No coffee shops found in the JSON. Expected OSM candidates, GeoJSON, 2GIS/Google/Yandex places, or an array of {name, lat, lon}."),
                null);
        }

        var existing = await repository.ListAllAsync(ct);
        var index = new CandidateIndex(existing);
        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var enriched = 0;
        var unchanged = 0;
        var invalid = 0;
        var enrichItems = new List<ImportShopEnrichmentItem>();

        foreach (var place in parsed)
        {
            if (place.Snapshot.Latitude is < -90 or > 90 || place.Snapshot.Longitude is < -180 or > 180)
            {
                invalid++;
                continue;
            }

            var match = index.FindMatch(place);
            if (match is not null)
            {
                if (match.EnrichFrom(place.Snapshot, now, place.GoogleMapsUri))
                    enriched++;
                else
                    unchanged++;

                AddEnrichment(enrichItems, match, place);
                continue;
            }

            var candidate = ShopImportCandidate.FromPlace(place.Source, place.Snapshot, now);
            if (!string.IsNullOrWhiteSpace(place.GoogleMapsUri))
                candidate.EnrichFrom(place.Snapshot, now, place.GoogleMapsUri);

            repository.Add(candidate);
            existing.Add(candidate);
            index.Add(candidate);
            inserted++;
            AddEnrichment(enrichItems, candidate, place);
        }

        var suggested = await ImportDuplicateScan.AddNewAsync(existing, suggestions, ct);
        await unitOfWork.SaveChangesAsync(ct);

        object? outbound = enrichItems.Count == 0
            ? null
            : new ImportShopEnrichmentEvent(enrichItems);

        return (Response<IngestImportFileResultDto>.Success(
            new IngestImportFileResultDto(parsed.Count, inserted, enriched, unchanged, invalid, suggested)), outbound);
    }

    /// <summary>
    /// Exact ExternalId lookup plus a coarse lat/lon grid for fuzzy matching. ShopPlaceMatcher never matches
    /// beyond 500 m, so only the 3x3 neighbouring cells need checking instead of every stored candidate
    /// (the previous linear scans made ingest O(places × candidates)).
    /// </summary>
    private sealed class CandidateIndex
    {
        // 0.01° latitude ≈ 1.1 km; 0.05° longitude ≥ 500 m everywhere below ~84° latitude.
        private const decimal LatCell = 0.01m;
        private const decimal LonCell = 0.05m;

        private readonly Dictionary<string, ShopImportCandidate> _byExternalId = new(StringComparer.Ordinal);
        private readonly Dictionary<(long, long), List<ShopImportCandidate>> _cells = new();

        public CandidateIndex(IEnumerable<ShopImportCandidate> candidates)
        {
            foreach (var candidate in candidates)
                Add(candidate);
        }

        public void Add(ShopImportCandidate candidate)
        {
            _byExternalId.TryAdd(candidate.ExternalId, candidate);

            var cell = Cell(candidate.Latitude, candidate.Longitude);
            if (!_cells.TryGetValue(cell, out var list))
                _cells[cell] = list = [];
            list.Add(candidate);
        }

        public ShopImportCandidate? FindMatch(ParsedImportPlace place)
        {
            if (_byExternalId.TryGetValue(place.Snapshot.ExternalId, out var exact))
                return exact;

            var (lat, lon) = Cell(place.Snapshot.Latitude, place.Snapshot.Longitude);
            for (var dLat = -1; dLat <= 1; dLat++)
            for (var dLon = -1; dLon <= 1; dLon++)
            {
                if (!_cells.TryGetValue((lat + dLat, lon + dLon), out var list))
                    continue;

                foreach (var candidate in list)
                {
                    if (candidate.IsSamePlaceAs(place.Snapshot))
                        return candidate;
                }
            }

            return null;
        }

        private static (long, long) Cell(decimal latitude, decimal longitude) =>
            ((long)Math.Floor(latitude / LatCell), (long)Math.Floor(longitude / LonCell));
    }

    private static void AddEnrichment(
        List<ImportShopEnrichmentItem> items,
        ShopImportCandidate candidate,
        ParsedImportPlace place)
    {
        var name = candidate.HasRealName
            ? candidate.Name!.Trim()
            : place.Snapshot.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        items.Add(new ImportShopEnrichmentItem(
            candidate.ResultingShopId,
            name,
            ShopPlaceMatcher.PreferRicherText(candidate.Address, place.Snapshot.Address),
            candidate.Latitude,
            candidate.Longitude,
            candidate.Phone ?? place.Snapshot.Phone,
            candidate.Website ?? place.Snapshot.Website,
            candidate.Instagram ?? place.Snapshot.Instagram));
    }
}
