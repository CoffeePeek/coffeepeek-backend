using System.Globalization;
using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.Places;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

public sealed class ShopImportCandidate : Entity<Guid>
{
    public const int GoogleCacheDays = 30;
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxPhoneLength = 200;
    public const int MaxWebsiteLength = 2048;
    public const int MaxInstagramLength = 255;
    public const int MaxOpeningHoursLength = 2000;
    public const int MaxCuisineLength = 200;
    public const int MaxBrandLength = 200;
    public const int MaxCheckDateLength = 32;
    public const int MaxGoogleMapsUriLength = 2048;

    public ImportSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string? Name { get; private set; }
    public string? Address { get; private set; }
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public string? Phone { get; private set; }
    public string? Website { get; private set; }
    public string? Instagram { get; private set; }
    public string? OpeningHours { get; private set; }
    public string? Cuisine { get; private set; }
    public string? Brand { get; private set; }
    public DateTimeOffset? OsmUpdatedAt { get; private set; }
    public int? OsmAgeDays { get; private set; }
    public string? CheckDate { get; private set; }
    public List<string> Signals { get; private set; } = [];
    public ImportCollectorBucket CollectorBucket { get; private set; }
    public ImportQueueStatus QueueStatus { get; private set; }
    public ImportCoffeeFocus? CoffeeFocus { get; private set; }
    public List<string> TagSlugs { get; private set; } = [];
    public ImportGoogleBusinessStatus? GoogleBusinessStatus { get; private set; }
    public string? GoogleMapsUri { get; private set; }
    public DateTimeOffset? GoogleFetchedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public Guid? ResultingShopId { get; private set; }
    public ImportRejectReason? RejectReason { get; private set; }
    public MenuDraft? Menu { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopImportCandidate()
    {
    }

    public bool HasRealName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;

            var trimmed = Name.Trim();
            return !trimmed.Equals("(unnamed)", StringComparison.OrdinalIgnoreCase)
                   && !trimmed.Equals("без имени", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsGoogleCacheFresh(DateTimeOffset now) =>
        GoogleFetchedAtUtc.HasValue && (now - GoogleFetchedAtUtc.Value).TotalDays <= GoogleCacheDays;

    public static ShopImportCandidate FromOsm(OsmCandidateSnapshot snapshot, DateTimeOffset now) =>
        FromPlace(ImportSource.Osm, snapshot, now);

    public static ShopImportCandidate FromPlace(ImportSource source, OsmCandidateSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(source))
            throw new DomainException("Unknown import source.");
        if (string.IsNullOrWhiteSpace(snapshot.ExternalId))
            throw new DomainException("ExternalId is required.");

        var candidate = new ShopImportCandidate
        {
            Id = Guid.NewGuid(),
            Source = source,
            ExternalId = Clip(snapshot.ExternalId, 64)!,
            QueueStatus = ImportQueueStatus.Pending
        };
        candidate.ApplyOsmFields(snapshot, now);
        if (source == ImportSource.File)
            candidate.AddSignal("import:file");
        return candidate;
    }





    public bool ImportedFromFile =>
        Source == ImportSource.File
        || Signals.Contains("import:file", StringComparer.OrdinalIgnoreCase);

    public void AddSignal(string signal)
    {
        if (string.IsNullOrWhiteSpace(signal))
            return;
        if (Signals.Contains(signal, StringComparer.OrdinalIgnoreCase))
            return;
        Signals.Add(signal.Trim());
    }

    public OsmCandidateSnapshot ToSnapshot()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["amenity"] = "cafe"
        };
        if (!string.IsNullOrWhiteSpace(Name))
            tags["name"] = Name;
        if (!string.IsNullOrWhiteSpace(Cuisine))
            tags["cuisine"] = Cuisine;
        if (!string.IsNullOrWhiteSpace(Brand))
            tags["brand"] = Brand;

        return new OsmCandidateSnapshot(
            ExternalId,
            Name,
            Address,
            Latitude,
            Longitude,
            Phone,
            Website,
            Instagram,
            OpeningHours,
            Cuisine,
            Brand,
            OsmUpdatedAt,
            CheckDate,
            tags);
    }

    public bool IsSamePlaceAs(OsmCandidateSnapshot snapshot) =>
        ShopPlaceMatcher.IsSamePlace(
            Name,
            Latitude,
            Longitude,
            snapshot.Name,
            snapshot.Latitude,
            snapshot.Longitude,
            Phone,
            snapshot.Phone,
            Instagram,
            snapshot.Instagram);

    /// <summary>
    /// Fills missing/weaker fields from another dump of the same place.
    /// Does not change queue status, so a pending OSM row stays pending for moderation.
    /// </summary>
    public bool EnrichFrom(OsmCandidateSnapshot snapshot, DateTimeOffset now, string? googleMapsUri = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changed = false;

        var nextName = PreferName(Name, snapshot.Name);
        if (nextName != Name)
        {
            Name = Clip(nextName, MaxNameLength);
            changed = true;
        }

        var nextAddress = ShopPlaceMatcher.PreferRicherText(Address, snapshot.Address);
        if (nextAddress != Address)
        {
            Address = Clip(nextAddress, MaxAddressLength);
            changed = true;
        }

        var nextPhone = ShopPlaceMatcher.PreferRicherText(Phone, snapshot.Phone);
        if (nextPhone != Phone)
        {
            Phone = Clip(nextPhone, MaxPhoneLength);
            changed = true;
        }

        var nextWebsite = ShopPlaceMatcher.PreferRicherText(Website, snapshot.Website);
        if (nextWebsite != Website)
        {
            Website = Clip(nextWebsite, MaxWebsiteLength);
            changed = true;
        }

        var nextInstagram = ShopPlaceMatcher.PreferRicherText(Instagram, snapshot.Instagram)
                            ?? OsmCafeClassifier.InstagramUrl(snapshot.Tags, snapshot.Website);
        if (nextInstagram != Instagram && !string.IsNullOrWhiteSpace(nextInstagram))
        {
            Instagram = Clip(nextInstagram, MaxInstagramLength);
            changed = true;
        }

        var nextHours = ShopPlaceMatcher.PreferRicherText(OpeningHours, snapshot.OpeningHours);
        if (nextHours != OpeningHours)
        {
            OpeningHours = Clip(nextHours, MaxOpeningHoursLength);
            changed = true;
        }

        var nextCuisine = ShopPlaceMatcher.PreferRicherText(Cuisine, snapshot.Cuisine);
        if (nextCuisine != Cuisine)
        {
            Cuisine = Clip(nextCuisine, MaxCuisineLength);
            changed = true;
        }

        var nextBrand = ShopPlaceMatcher.PreferRicherText(Brand, snapshot.Brand);
        if (nextBrand != Brand)
        {
            Brand = Clip(nextBrand, MaxBrandLength);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(GoogleMapsUri) && !string.IsNullOrWhiteSpace(googleMapsUri))
        {
            GoogleMapsUri = Clip(googleMapsUri, MaxGoogleMapsUriLength);
            changed = true;
        }

        var classified = OsmCafeClassifier.Classify(snapshot.Tags, snapshot.OsmUpdatedAt, now);
        var nextSignals = Signals
            .Concat(classified.Signals)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (nextSignals.Count != Signals.Count || nextSignals.Except(Signals, StringComparer.OrdinalIgnoreCase).Any())
        {
            Signals = nextSignals;
            changed = true;
        }

        if (BucketRank(classified.Bucket) < BucketRank(CollectorBucket))
        {
            CollectorBucket = classified.Bucket;
            changed = true;
        }

        if (changed && !Signals.Contains("import:merged", StringComparer.OrdinalIgnoreCase))
            Signals.Add("import:merged");

        return changed;
    }

    private static string? PreferName(string? current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return current;
        if (string.IsNullOrWhiteSpace(current)
            || current.Trim().Equals("(unnamed)", StringComparison.OrdinalIgnoreCase)
            || current.Trim().Equals("без имени", StringComparison.OrdinalIgnoreCase))
            return incoming.Trim();
        return current;
    }

    private static int BucketRank(ImportCollectorBucket bucket) => bucket switch
    {
        ImportCollectorBucket.LikelySpecialty => 0,
        ImportCollectorBucket.Priority => 1,
        ImportCollectorBucket.Review => 2,
        ImportCollectorBucket.LikelyNoise => 3,
        ImportCollectorBucket.AutoReject => 4,
        ImportCollectorBucket.Stale => 5,
        _ => 9
    };

    public void RefreshFromOsm(OsmCandidateSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (QueueStatus is ImportQueueStatus.Published or ImportQueueStatus.Skipped)
            return;

        ApplyOsmFields(snapshot, now);
    }

    public void Decide(
        ImportQueueStatus status,
        ImportCoffeeFocus? focus,
        IReadOnlyList<string>? tagSlugs,
        Guid reviewerId,
        bool overrideClosed,
        DateTimeOffset now,
        ImportRejectReason? rejectReason = null)
    {
        if (reviewerId == Guid.Empty)
            throw new DomainException("Reviewer is required.");

        if (ResultingShopId is not null)
            throw new DomainException("Candidate is already published to the catalog.");

        // Publish is in flight (Shops has not replied yet). Re-publishing is a safe retry because Shops dedupes by
        // candidate id, but moving away from Published would orphan the shop that is about to be created.
        if (QueueStatus == ImportQueueStatus.Published && status != ImportQueueStatus.Published)
            throw new DomainException("Candidate publish is in progress; it cannot be moved to another status.");

        if (status == ImportQueueStatus.Rejected)
        {
            if (rejectReason is null)
                throw new DomainException("Reject reason is required when rejecting a candidate.");
        }

        if (status == ImportQueueStatus.Published)
        {
            if (focus is null)
                throw new DomainException("Coffee shop type is required to publish.");

            if (!HasRealName)
                throw new DomainException("Cannot publish a candidate without a real name.");

            if (GoogleBusinessStatus == ImportGoogleBusinessStatus.ClosedPermanently && !overrideClosed)
                throw new DomainException(
                    "Google reports this place as permanently closed. Pass overrideClosed=true to publish anyway.");
        }

        QueueStatus = status;
        CoffeeFocus = status == ImportQueueStatus.Published ? focus : focus ?? CoffeeFocus;
        TagSlugs = NormalizeTagSlugs(tagSlugs, CoffeeFocus);
        RejectReason = status == ImportQueueStatus.Rejected ? rejectReason : null;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = now;
    }

    public void AttachPublishedShop(Guid shopId)
    {
        if (shopId == Guid.Empty)
            throw new DomainException("ShopId cannot be empty.");

        ResultingShopId = shopId;
        QueueStatus = ImportQueueStatus.Published;
    }

    public void ApplyGoogleStatus(
        ImportGoogleBusinessStatus status,
        string? mapsUri,
        DateTimeOffset fetchedAt)
    {
        GoogleBusinessStatus = status;
        GoogleMapsUri = string.IsNullOrWhiteSpace(mapsUri)
            ? GoogleMapsUri
            : Clip(mapsUri, MaxGoogleMapsUriLength);
        GoogleFetchedAtUtc = fetchedAt;
    }

    public void PatchContacts(
        string? instagram,
        bool patchInstagram,
        string? phone,
        bool patchPhone,
        string? website,
        bool patchWebsite,
        string? openingHours,
        bool patchOpeningHours)
    {
        if (ResultingShopId is not null)
            throw new DomainException("Cannot edit contacts after the candidate is published to the catalog.");

        if (patchInstagram)
            Instagram = Clip(ImportContactNormalizer.Instagram(instagram), MaxInstagramLength);

        if (patchPhone)
            Phone = Clip(phone, MaxPhoneLength);

        if (patchWebsite)
            Website = Clip(ImportContactNormalizer.Website(website, MaxWebsiteLength), MaxWebsiteLength);

        if (patchOpeningHours)
            OpeningHours = Clip(openingHours, MaxOpeningHoursLength);
    }

    public IReadOnlyList<ImportSuggestedTag> GetSuggestedTags() =>
        ImportDossierAdvisor.SuggestTags(Signals, Cuisine);

    public ImportCoffeeFocus? GetSuggestedFocus() =>
        ImportDossierAdvisor.SuggestFocus(Signals, CollectorBucket);

    public ImportGaps GetGaps() =>
        ImportDossierAdvisor.Gaps(Instagram, Phone, Website, OpeningHours);

    public ImportResearchLinks GetResearchLinks()
    {
        var lat = Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = Longitude.ToString(CultureInfo.InvariantCulture);

        return new ImportResearchLinks(
            Instagram: Instagram,
            InstagramSearch: null,
            GoogleMaps: GoogleMapsUri ?? $"https://www.google.com/maps/@{lat},{lon},18z",
            YandexMaps:
                $"https://yandex.by/maps/?ll={lon},{lat}&z=18&mode=whatshere&whatshere[point]={lon},{lat}&whatshere[zoom]=18",
            YandexImages: $"https://yandex.by/maps/?ll={lon},{lat}&z=18&l=stv",
            OsmHistory: OsmHistoryUrl() ?? "",
            YandexEmbed: $"https://yandex.ru/map-widget/v1/?ll={lon},{lat}&z=18&pt={lon},{lat},pm2rdm",
            GoogleEmbed: $"https://maps.google.com/maps?q={lat},{lon}&z=18&output=embed",
            StreetView: $"https://www.google.com/maps/@?api=1&map_action=pano&viewpoint={lat},{lon}",
            StreetViewEmbed:
                $"https://maps.google.com/maps?q=&layer=c&cbll={lat},{lon}&cbp=11,0,0,0,0&output=embed");
    }

    public string PublishAddress()
    {
        if (!string.IsNullOrWhiteSpace(Address))
            return Address.Trim();

        return "Минск";
    }

    public void AttachMenuPhotos(
        IReadOnlyList<(string FileName, string ContentType, string StorageKey, long SizeBytes)> photos,
        DateTime utcNow)
    {
        Menu ??= MenuDraft.CreateEmpty();
        Menu.AttachPhotos(photos, utcNow);
    }

    public void RequestMenuParse()
    {
        if (Menu is null || Menu.Photos.Count == 0)
            throw new DomainException("Attach menu photos before parsing.");

        Menu.MarkParsePending();
    }

    public void ApplyMenuParseResult(
        bool success,
        string? error,
        int? suggestedPriceRange,
        IReadOnlyList<MenuDraftItem> items,
        IReadOnlyList<MenuDraftUnmatched> unmatched,
        DateTime utcNow)
    {
        Menu ??= MenuDraft.CreateEmpty();
        Menu.ApplyParseResult(success, error, suggestedPriceRange, items, unmatched, utcNow);
    }

    public void ReplaceMenuItems(
        IReadOnlyList<MenuDraftItem> items,
        DateTime utcNow)
    {
        Menu ??= MenuDraft.CreateEmpty();
        Menu.ApplyManualItems(items, utcNow);
    }

    private void ApplyOsmFields(OsmCandidateSnapshot snapshot, DateTimeOffset now)
    {
        var (bucket, signals, _) = OsmCafeClassifier.Classify(snapshot.Tags, snapshot.OsmUpdatedAt, now);

        Name = Clip(snapshot.Name, MaxNameLength);
        Address = Clip(snapshot.Address, MaxAddressLength);
        Latitude = snapshot.Latitude;
        Longitude = snapshot.Longitude;
        Phone = Clip(snapshot.Phone, MaxPhoneLength);
        Website = Clip(snapshot.Website, MaxWebsiteLength);
        Instagram = Clip(
            snapshot.Instagram ?? OsmCafeClassifier.InstagramUrl(snapshot.Tags, snapshot.Website),
            MaxInstagramLength);
        OpeningHours = Clip(snapshot.OpeningHours, MaxOpeningHoursLength);
        Cuisine = Clip(snapshot.Cuisine, MaxCuisineLength);
        Brand = Clip(snapshot.Brand, MaxBrandLength);
        OsmUpdatedAt = snapshot.OsmUpdatedAt;
        OsmAgeDays = snapshot.OsmUpdatedAt.HasValue
            ? (int)(now - snapshot.OsmUpdatedAt.Value).TotalDays
            : null;
        CheckDate = Clip(snapshot.CheckDate, MaxCheckDateLength);
        Signals = signals.ToList();
        CollectorBucket = bucket;
    }



    private static List<string> NormalizeTagSlugs(IReadOnlyList<string>? tagSlugs, ImportCoffeeFocus? focus)
    {
        var slugs = (tagSlugs ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (focus == ImportCoffeeFocus.Specialty && !slugs.Contains("specialty"))
            slugs.Add("specialty");

        if (focus != ImportCoffeeFocus.Specialty)
            slugs.Remove("specialty");

        return slugs;
    }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private string? OsmHistoryUrl()
    {
        if (Source != ImportSource.Osm)
            return null;

        var parts = ExternalId.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        var type = parts[0];
        if (type is not ("node" or "way" or "relation"))
            return null;

        return $"https://www.openstreetmap.org/{type}/{parts[1]}/history";
    }
}
