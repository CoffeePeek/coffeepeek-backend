using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class CoffeeImportService(ICoffeeImportStorage storage, IOptions<CoffeeImportOptions> importOptions, ILogger logger) : ICoffeeImportWriter
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public async Task RecordFailureAsync(CoffeeImportFailureDto failure, CancellationToken ct)
    {
        if (failure is null || string.IsNullOrEmpty(failure.SourceKey) || !importOptions.Value.Roasters.ContainsKey(failure.SourceKey) ||
            failure.SnapshotId is null || !Regex.IsMatch(failure.SnapshotId, "\\A[a-f0-9]{64}\\z") ||
            failure.CollectedAtUtc == default || failure.CollectedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10) ||
            failure.Stage is not ("collect" or "photos" or "apply") || string.IsNullOrWhiteSpace(failure.Error) || failure.Error.Length > 2000)
            throw new CoffeeCatalogException("Invalid import failure report.");
        var run = new CoffeeImportRun(failure.SourceKey, failure.SnapshotId, failure.SnapshotId);
        run.MarkFailed(failure.CollectedAtUtc.UtcDateTime, failure.Stage + ": " + failure.Error[..Math.Min(failure.Error.Length, 1980)]);
        await storage.RecordFailureAsync(run, ct);
    }

    public async Task<CoffeeImportResult> ImportAsync(CoffeeSnapshotImportRequest request, CancellationToken ct)
    {
        ValidateSnapshot(request);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request.Snapshot, WireJson))));
        var collectedAt = request.Snapshot.CollectedAtUtc.UtcDateTime;
        try
        {
            return await storage.InTransactionAsync(async () =>
            {
                var brands = importOptions.Value.Roasters[request.SourceKey];
                var roasterIds = brands.Values.Distinct().ToArray();
                var state = await storage.LoadAsync(request.SourceKey, request.SnapshotId, roasterIds, ct);
                var previous = state.PreviousRun;
                if (previous is not null && !previous.CanApplyPayload(payloadHash))
                    throw new CoffeeCatalogException("Snapshot identity was reused for different content.", 409);
                if (previous?.Status == "Applied") return Result(previous);
                var last = state.LastAppliedRun;
                if (last is not null && last.CollectedAtUtc >= collectedAt)
                    throw new CoffeeCatalogException("A newer source snapshot has already been applied.", 409);

                if (state.ExistingRoasterIds.Length != roasterIds.Length)
                    throw new CoffeeCatalogException("Source mapping references a missing roaster.");
                var knownCountries = state.CountryCodes;
                var activeClassification = state.ActiveClassification;
                var sources = state.Sources;
                var newProducts = new List<CoffeeProduct>();
                var newVariants = new List<CoffeeVariant>();
                var newPhotos = new List<CoffeeProductPhoto>();
                var removedPhotos = new List<CoffeeProductPhoto>();
                var byProduct = sources.ToDictionary(s => s.ExternalProductId, StringComparer.Ordinal);
                var byVariant = sources.SelectMany(s => s.Variants.Select(v => (v.ExternalVariantId, Source: s)))
                    .ToDictionary(v => v.ExternalVariantId, v => v.Source, StringComparer.Ordinal);
                var observed = new HashSet<Guid>();
                var added = 0;
                foreach (var item in request.Snapshot.Products)
                {
                    var roasterId = brands[item.RoasterName ?? string.Empty];
                    byProduct.TryGetValue(item.SourceProductId, out var source);
                    var matches = item.Variants.Where(v => byVariant.ContainsKey(v.ExternalVariantId))
                        .Select(v => byVariant[v.ExternalVariantId]).DistinctBy(s => s.Id).ToArray();
                    if (matches.Length > 1 || (source is not null && matches.Any(s => s.Id != source.Id)))
                        throw new CoffeeCatalogException("SKU group conflicts with existing product identities; manual mapping is required.", 409);
                    source ??= matches.SingleOrDefault();
                    if (source is not null && source.Product.RoasterId != roasterId)
                        throw new CoffeeCatalogException("Existing coffee belongs to a different roaster.", 409);
                    var data = SnapshotContent(item);
                    if (source is null)
                    {
                        var product = new CoffeeProduct(roasterId, data);
                        product.SetPublicSlug(CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Candidate(product.Name, product.Id, 1));
                        source = new CoffeeProductSource(product, request.SourceKey, item.SourceProductId, item.SourceUrl);
                        product.AttachSource(source); newProducts.Add(product); sources.Add(source); added++;
                    }
                    if (!observed.Add(source.Id)) throw new CoffeeCatalogException("Two source cards resolve to the same product group.", 409);
                    source.UpdateMetadata(item.SourceProductId, item.SourceUrl, item.RoasterName,
                        item.SellerName ?? item.RoasterName, JsonSerializer.Serialize(item, WireJson), collectedAt);
                    var warnings = item.ReviewWarnings.ToList();
                    source.Product.ApplyContent(data, fromImport: true);
                    source.Product.ApplyClassification(CoffeeClassificationNormalizer.ActiveOnly(
                        CoffeeClassificationNormalizer.Normalize(item), activeClassification), fromImport: true);
                    var countryCodes = new List<string>();
                    foreach (var origin in item.Origins)
                    {
                        if (!knownCountries.Contains(origin.CountryCode)) warnings.Add("country_not_in_catalog:" + origin.CountryCode);
                        else if (string.IsNullOrWhiteSpace(origin.EvidenceField) || (origin.EvidenceField.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                            (item.CompositionKind == "blend" || item.Origins.Length != 1 ||
                             item.ReviewWarnings.Contains("ambiguous_congo_country", StringComparer.Ordinal))))
                            warnings.Add("country_requires_confirmation:" + origin.CountryCode);
                        else countryCodes.Add(origin.CountryCode);
                    }
                    source.Product.ApplyImportedCountries(countryCodes);
                    var seenVariants = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var offer in item.Variants)
                    {
                        seenVariants.Add(offer.ExternalVariantId);
                        var row = source.Variants.SingleOrDefault(v => v.ExternalVariantId == offer.ExternalVariantId);
                        if (row is null)
                        {
                            row = new CoffeeVariant(source, offer.ExternalVariantId, offer.WeightGrams, offer.Price, offer.Currency);
                            source.AttachVariant(row);
                            newVariants.Add(row);
                        }
                        row.UpdateOffer(offer.WeightGrams, offer.Price, offer.Currency,
                            offer.AvailableForPurchase ? CoffeeAvailability.InStock : CoffeeAvailability.OutOfStock,
                            item.AvailabilityScope, collectedAt, Attribute(offer.SourceAttributes, "grind"),
                            Attribute(offer.SourceAttributes, "roastPurpose"),
                            offer.SourceAttributes.ValueKind == JsonValueKind.Undefined ? "{}" : offer.SourceAttributes.GetRawText());
                        var brewCode = CoffeeClassificationNormalizer.NormalizeBrewPurpose(row.RoastPurpose);
                        if (brewCode is not null && activeClassification.Any(v => v.GroupCode == "brew" && v.Code == brewCode))
                            row.SetBrewPurpose(brewCode, false, fromImport: true);
                    }
                    foreach (var missing in source.Variants.Where(v => !seenVariants.Contains(v.ExternalVariantId)))
                        missing.MarkAvailability(CoffeeAvailability.Unknown);
                    if (item.StoredPhotos.Length > 0 && !source.Product.IsProtected("Photos"))
                    {
                        var keys = item.StoredPhotos.Select(p => p.StorageKey).ToHashSet(StringComparer.Ordinal);
                        removedPhotos.AddRange(source.RemovePhotosExcept(keys));
                        var index = 0;
                        foreach (var image in item.StoredPhotos.DistinctBy(p => p.StorageKey))
                        {
                            var row = source.Photos.SingleOrDefault(p => p.StorageKey == image.StorageKey);
                            if (row is null)
                            {
                                row = new CoffeeProductPhoto(image.StorageKey, image.SourceUrl!, image.FileName,
                                    image.ContentType, image.SizeBytes, image.Id, index);
                                source.AttachPhoto(row);
                                newPhotos.Add(row);
                            }
                            source.UpdatePhoto(row, image.SourceUrl!, image.FileName, image.ContentType, image.SizeBytes, image.Id, index++);
                        }
                    }
                    if (item.ImageUrls.Length > 0 && item.StoredPhotos.Length == 0) warnings.Add("photos_not_imported");
                    if (item.ImageUrls.Length == 0 && source.Photos.Count == 0) warnings.Add("photos_not_provided");
                    source.SetReviewWarnings(warnings);
                }

                // ponytail: HTML without an independent counter never retires missing offers automatically;
                // add per-source reconciliation approval if automatic retirement becomes necessary.
                var reconcile = request.Snapshot.Coverage.Length > 0 && request.Snapshot.Coverage.All(c =>
                    c.ExpectedProducts.HasValue && c.ExpectedProducts == c.DiscoveredProducts) &&
                    (last is null || request.Snapshot.Products.Length * 2 >= last.Products);
                if (reconcile)
                    foreach (var missing in sources.Where(s => !observed.Contains(s.Id)))
                    {
                        missing.MarkAvailability(CoffeeAvailability.Unknown);
                    }
                // Explicit unavailable rows are safe even when an HTML catalogue has no total counter.
                foreach (var excluded in request.Snapshot.Excluded.Where(e => e.Reason == "not_purchasable"))
                {
                    var source = sources.SingleOrDefault(s => s.ExternalProductId == excluded.SourceProductId);
                    if (source is null) continue;
                    source.MarkAvailability(CoffeeAvailability.OutOfStock, collectedAt);
                }
                var run = previous ?? new CoffeeImportRun(request.SourceKey, request.SnapshotId, payloadHash);
                run.MarkApplied(payloadHash, collectedAt, request.Snapshot.Products.Length,
                    request.Snapshot.Products.Sum(p => p.Variants.Length), added, reconcile);
                await storage.SaveAsync(new(newProducts, newVariants, newPhotos, removedPhotos, run, previous is null), ct);
                return Result(run);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                var run = new CoffeeImportRun(request.SourceKey, request.SnapshotId, payloadHash);
                run.MarkFailed(collectedAt, ex.Message[..Math.Min(ex.Message.Length, 2000)]);
                await storage.RecordFailureAsync(run, ct);
            }
            catch (Exception logError) { logger.LogError(logError, "Could not record failed coffee import for {Source}", request.SourceKey); }
            throw;
        }
    }

    private void ValidateSnapshot(CoffeeSnapshotImportRequest request)
    {
        if (request is null || string.IsNullOrEmpty(request.SourceKey) || !importOptions.Value.Roasters.TryGetValue(request.SourceKey, out var brands) || brands.Count == 0)
            throw new CoffeeCatalogException("Source has no explicit roaster mapping.");
        if (request.SnapshotId is null || !Regex.IsMatch(request.SnapshotId, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
            throw new CoffeeCatalogException("Snapshot ID must be a SHA-256 identifier.");
        var snapshot = request.Snapshot;
        if (snapshot is null || snapshot.SchemaVersion != 1 || !snapshot.Complete || snapshot.Products is null ||
            snapshot.Products.Length > 2000 || snapshot.Coverage is null || snapshot.Excluded is null ||
            snapshot.CollectedAtUtc == default || snapshot.CollectedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new CoffeeCatalogException("Incomplete, future-dated or unsupported snapshot.");
        ValidateSourceUrl(snapshot.Source, request.SourceKey);
        if (snapshot.Coverage.Any(c => c is null || c.DiscoveredProducts < 0 || c.ExpectedProducts < 0) ||
            snapshot.Excluded.Any(e => e is null || e.SourceProductId?.Length > 300 ||
                (e.Reason == "not_purchasable" && string.IsNullOrWhiteSpace(e.SourceProductId))))
            throw new CoffeeCatalogException("Invalid coverage or excluded product identity.");
        if (snapshot.Products.Length == 0 && !snapshot.Excluded.Any(e => e.Reason == "not_purchasable"))
            throw new CoffeeCatalogException("Empty snapshot must explicitly identify unavailable products.");
        var products = new HashSet<string>(StringComparer.Ordinal);
        var variants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in snapshot.Products)
        {
            if (p is null || p.SourceKey != request.SourceKey || string.IsNullOrWhiteSpace(p.SourceProductId) ||
                p.SourceProductId.Length > 300 || !products.Add(p.SourceProductId) || !brands.ContainsKey(p.RoasterName ?? string.Empty) ||
                p.Origins is null || p.Origins.Length > 100 || p.ReviewWarnings is null || p.ReviewWarnings.Length > 100 ||
                p.ReviewWarnings.Any(w => w is null || w.Length > 500) || p.ImageUrls is null || p.StoredPhotos is null || p.Variants is null ||
                p.Variants.Length is < 1 or > 200 || p.ImageUrls.Length > 64 || p.StoredPhotos.Length > 64 ||
                p.AvailabilityScope is not ("online" or "in_store"))
                throw new CoffeeCatalogException("Invalid product identity, brand, scope or collections.");
            ValidateSourceUrl(p.SourceUrl, request.SourceKey);
            if (p.ImageUrls.Any(u => u is null || u.Length > 2048 || !Uri.TryCreate(u, UriKind.Absolute, out var image) ||
                image.Scheme != "https" || !image.IsDefaultPort || !string.IsNullOrEmpty(image.UserInfo)))
                throw new CoffeeCatalogException("Invalid image URL.");
            _ = new CoffeeProduct(brands[p.RoasterName ?? string.Empty], SnapshotContent(p));
            if (p.Origins.Any(o => o is null || !Regex.IsMatch(o.CountryCode ?? "", "^[A-Z]{2}$")))
                throw new CoffeeCatalogException("Invalid country code.");
            foreach (var v in p.Variants)
                if (v is null || string.IsNullOrWhiteSpace(v.ExternalVariantId) || v.ExternalVariantId.Length > 300 ||
                    !variants.Add(v.ExternalVariantId) || v.WeightGrams is <= 0 or > 100000 ||
                    v.Price is <= 0 or > 999999999 || decimal.Round(v.Price, 2) != v.Price || v.Currency is not ("BYN" or "RUB"))
                    throw new CoffeeCatalogException("Invalid or duplicate variant, weight, price or currency.");
            foreach (var photo in p.StoredPhotos)
                if (photo is null || photo.Id == Guid.Empty || photo.SizeBytes is <= 0 or > 8388608 ||
                    photo.StorageKey is null || !Regex.IsMatch(photo.StorageKey, "^coffees/[a-f0-9]{64}\\.(jpg|png|webp|gif|avif)$") ||
                    photo.ContentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif") ||
                    string.IsNullOrWhiteSpace(photo.FileName) || photo.FileName.Length > 255 ||
                    photo.SourceUrl is null || photo.SourceUrl.Length > 2048 || !p.ImageUrls.Contains(photo.SourceUrl))
                    throw new CoffeeCatalogException("Invalid stored coffee photo metadata.");
        }
    }
    private static void ValidateSourceUrl(string url, string source)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IdnHost != source ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || url.Length > 2048)
            throw new CoffeeCatalogException("Unexpected source URL.");
    }
    private static CoffeeContent SnapshotContent(CoffeeSnapshotProduct p) => CoffeeContentMapper.ToDomain(new(p.Name, p.Description,
        p.ProductKind, p.ProductForm, p.CompositionKind, p.Processing, p.RoastLevel, p.Acidity, p.Body,
        p.QGraderScoreRaw, p.TasteDescriptors, p.BrewRecommendations, p.GrindOptions, p.Features));
    private static string? Attribute(JsonElement attrs, string key) => attrs.ValueKind == JsonValueKind.Object &&
        attrs.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static CoffeeImportResult Result(CoffeeImportRun r) => new(r.Id, r.SourceKey, r.SnapshotId,
        r.Status, r.Products, r.Variants, r.Added, r.MissingAvailabilityApplied, r.AppliedAtUtc);
}
