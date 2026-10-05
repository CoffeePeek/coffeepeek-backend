using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Kernel.Options;
using DomainSource = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportSource;
using DomainBucket = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportCollectorBucket;
using DomainFocus = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportCoffeeFocus;
using DomainGoogle = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportGoogleBusinessStatus;
using DomainStatus = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportQueueStatus;
using DomainRejectReason = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportRejectReason;
using ContractRejectReason = CoffeePeek.Contract.Enums.ImportRejectReason;

namespace CoffeePeek.Moderation.Application.Features.Import;

public static class ShopImportCandidateMapper
{
    public static ShopImportCandidateDto ToDto(
        ShopImportCandidate candidate,
        MediaPublicUrlOptions? mediaOptions = null)
    {
        var links = candidate.GetResearchLinks();
        return new ShopImportCandidateDto(
            candidate.Id,
            candidate.Source.ToString(),
            candidate.ExternalId,
            candidate.Name,
            candidate.Address,
            candidate.Latitude,
            candidate.Longitude,
            candidate.Phone,
            candidate.Website,
            candidate.Instagram,
            candidate.OpeningHours,
            candidate.Cuisine,
            candidate.Brand,
            candidate.OsmUpdatedAt,
            candidate.OsmAgeDays,
            candidate.CheckDate,
            candidate.Signals,
            (Contract.Enums.ImportCollectorBucket)(int)candidate.CollectorBucket,
            (Contract.Enums.ImportQueueStatus)(int)candidate.QueueStatus,
            candidate.CoffeeFocus is null ? null : (CoffeeShopType)(int)candidate.CoffeeFocus.Value,
            candidate.TagSlugs,
            candidate.GoogleBusinessStatus is null
                ? null
                : (GoogleBusinessStatus)(int)candidate.GoogleBusinessStatus.Value,
            candidate.GoogleFetchedAtUtc,
            candidate.GoogleBusinessStatus == DomainGoogle.ClosedPermanently,
            candidate.ReviewedByUserId,
            candidate.ReviewedAtUtc,
            candidate.ResultingShopId,
            candidate.RejectReason is null
                ? null
                : (ContractRejectReason)(int)candidate.RejectReason.Value,
            new ImportResearchLinksDto(
                links.Instagram,
                links.InstagramSearch,
                links.GoogleMaps,
                links.YandexMaps,
                links.YandexImages,
                links.OsmHistory,
                links.YandexEmbed,
                links.GoogleEmbed,
                links.StreetView,
                links.StreetViewEmbed),
            candidate.CreatedAtUtc,
            candidate.ImportedFromFile,
            ToFacts(candidate),
            candidate.GetSuggestedTags()
                .Select(t => new SuggestedTagDto(t.Slug, t.Why))
                .ToList(),
            candidate.GetSuggestedFocus() is { } suggestedType
                ? (CoffeeShopType)(int)suggestedType
                : null,
            ToGaps(candidate.GetGaps()),
            MenuDraftMapper.ToDto(candidate.Menu, mediaOptions ?? new MediaPublicUrlOptions()));
    }

    public static ImportDuplicateCandidateDto ToDuplicateDto(ShopImportCandidate candidate) =>
        new(
            candidate.Id,
            candidate.Source.ToString(),
            candidate.ExternalId,
            candidate.Name,
            candidate.Address,
            candidate.Latitude,
            candidate.Longitude,
            candidate.Phone,
            candidate.Website,
            candidate.Instagram,
            candidate.QueueStatus.ToString(),
            candidate.ImportedFromFile,
            candidate.ResultingShopId);

    public static ImportDuplicateSuggestionDto ToDto(
        ShopImportDuplicateSuggestion suggestion,
        ShopImportCandidate left,
        ShopImportCandidate right) =>
        new(
            suggestion.Id,
            suggestion.Score,
            suggestion.DistanceMeters,
            suggestion.Reasons,
            suggestion.Status.ToString(),
            ToDuplicateDto(left),
            ToDuplicateDto(right),
            suggestion.ReviewedByUserId,
            suggestion.ReviewedAtUtc);

    public static DomainStatus ToDomain(Contract.Enums.ImportQueueStatus status) => (DomainStatus)(int)status;

    public static DomainFocus ToDomain(CoffeeShopType type) => (DomainFocus)(int)type;

    public static DomainBucket ToDomain(Contract.Enums.ImportCollectorBucket bucket) => (DomainBucket)(int)bucket;

    public static DomainRejectReason ToDomain(ContractRejectReason reason) =>
        (DomainRejectReason)(int)reason;

    public static IReadOnlyList<string> ToFacts(ShopImportCandidate candidate)
    {
        var facts = new List<string>
        {
            candidate.Source switch
            {
                DomainSource.Osm => "Source: OpenStreetMap",
                DomainSource.File => "Source: file",
                _ => $"Source: {candidate.Source}"
            }
        };

        if (candidate.GoogleBusinessStatus == DomainGoogle.ClosedPermanently)
            facts.Add("Google: permanently closed");
        else if (candidate.GoogleBusinessStatus == DomainGoogle.ClosedTemporarily)
            facts.Add("Google: temporarily closed");
        else if (candidate.GoogleBusinessStatus == DomainGoogle.Operational)
            facts.Add("Google: operational");
        else if (candidate.GoogleBusinessStatus == DomainGoogle.NotFound)
            facts.Add("Google: place not found");
        else if (candidate.GoogleBusinessStatus == DomainGoogle.Far)
            facts.Add("Google: nearest match is far from the point");

        foreach (var signal in candidate.Signals)
        {
            if (signal.StartsWith(ImportVerificationSignals.RecheckReasonPrefix, StringComparison.Ordinal))
            {
                var reason = signal[ImportVerificationSignals.RecheckReasonPrefix.Length..];
                facts.Add("Перепроверить: " + (reason switch
                {
                    "missing-address" => "уточнить улицу, дом и вход",
                    "verify-address" => "сверить адрес и корпус",
                    "verify-name-type" => "сверить название и формат заведения",
                    "listing-without-address" => "найти карточку с точным адресом",
                    "insufficient-evidence" => "найти актуальное независимое подтверждение",
                    _ => "проверить данные кандидата"
                }));
                continue;
            }

            if (signal is "name:to-go-chain" or "name:chain")
                facts.Add("Looks like a to-go chain");
            else if (signal is "osm:vending_machine" or "name:vending-like")
                facts.Add("Looks like a vending machine");
            else if (signal is "name:canteen")
                facts.Add("Looks like a canteen / buffet");
        }

        if (!string.IsNullOrWhiteSpace(candidate.Instagram))
            facts.Add("Has Instagram");
        else if (!string.IsNullOrWhiteSpace(candidate.Website))
            facts.Add("Has a website");

        return facts;
    }

    public static ImportGapsDto ToGaps(ImportGaps gaps) =>
        new(gaps.Instagram, gaps.Phone, gaps.Website, gaps.Hours, gaps.Photo);

    public static ImportDossierHintsDto DossierHints() =>
        new(ImportDossierAdvisor.YandexHints
            .Select(h => new YandexTagHintDto(
                h.Label,
                h.Slug,
                h.Focus is null ? null : (CoffeeShopType)(int)h.Focus.Value))
            .ToList());
}
