using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoffeePeek.Shops.Persistance.PublicAddresses;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed partial class CoffeeCatalogStore
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public async Task RecordFailureAsync(CoffeeImportFailureDto failure, CancellationToken ct)
    {
        if (failure is null || string.IsNullOrEmpty(failure.SourceKey) || !importOptions.Value.Roasters.ContainsKey(failure.SourceKey) ||
            failure.SnapshotId is null || !Regex.IsMatch(failure.SnapshotId, "\\A[a-f0-9]{64}\\z") ||
            failure.CollectedAtUtc == default || failure.CollectedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10) ||
            failure.Stage is not ("collect" or "photos" or "apply") || string.IsNullOrWhiteSpace(failure.Error) || failure.Error.Length > 2000)
            throw new CoffeeCatalogException("Invalid import failure report.");
        if (await db.CoffeeImportRuns.AnyAsync(r => r.SourceKey == failure.SourceKey && r.SnapshotId == failure.SnapshotId, ct)) return;
        db.CoffeeImportRuns.Add(new CoffeeImportRun { SourceKey = failure.SourceKey, SnapshotId = failure.SnapshotId,
            PayloadHash = failure.SnapshotId, CollectedAtUtc = failure.CollectedAtUtc.UtcDateTime, AppliedAtUtc = DateTime.UtcNow,
            Status = "Failed", Error = failure.Stage + ": " + failure.Error[..Math.Min(failure.Error.Length, 1980)] });
        await db.SaveChangesAsync(ct);
    }

    public async Task<CoffeeImportResult> ImportAsync(CoffeeSnapshotImportRequest request, CancellationToken ct)
    {
        ValidateSnapshot(request);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request.Snapshot, WireJson))));
        var collectedAt = request.Snapshot.CollectedAtUtc.UtcDateTime;
        try
        {
            Func<Task<CoffeeImportResult>> operation = () => PublicAddressTransaction.RunAsync(db, async () =>
            {
                if (db.Database.IsRelational())
                {
                    var key = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(request.SourceKey)), 0);
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
                }
                var previous = await db.CoffeeImportRuns.SingleOrDefaultAsync(r => r.SourceKey == request.SourceKey && r.SnapshotId == request.SnapshotId, ct);
                if (previous is not null && previous.PayloadHash != payloadHash)
                    throw new CoffeeCatalogException("Snapshot identity was reused for different content.", 409);
                if (previous?.Status == "Applied") return Result(previous);
                var last = await db.CoffeeImportRuns.Where(r => r.SourceKey == request.SourceKey && r.Status == "Applied")
                    .OrderByDescending(r => r.CollectedAtUtc).FirstOrDefaultAsync(ct);
                if (last is not null && last.CollectedAtUtc >= collectedAt)
                    throw new CoffeeCatalogException("A newer source snapshot has already been applied.", 409);

                var brands = importOptions.Value.Roasters[request.SourceKey];
                var roasterIds = brands.Values.Distinct().ToArray();
                if (await db.Roasters.CountAsync(r => roasterIds.Contains(r.Id), ct) != roasterIds.Length)
                    throw new CoffeeCatalogException("Source mapping references a missing roaster.");
                var knownCountries = await db.Countries.Select(c => c.Code).ToArrayAsync(ct);
                var activeClassification = await db.CoffeeFilterValues.AsNoTracking().Where(v => v.IsActive).ToArrayAsync(ct);
                var sources = await db.CoffeeProductSources.Where(s => s.SourceKey == request.SourceKey)
                    .Include(s => s.Product).ThenInclude(p => p.Countries)
                    .Include(s => s.Variants).Include(s => s.Photos).AsSplitQuery().ToListAsync(ct);
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
                        source = new CoffeeProductSource { Product = product, CoffeeProductId = product.Id, SourceKey = request.SourceKey };
                        product.Sources.Add(source); db.CoffeeProducts.Add(product); sources.Add(source); added++;
                    }
                    if (!observed.Add(source.Id)) throw new CoffeeCatalogException("Two source cards resolve to the same product group.", 409);
                    source.ExternalProductId = item.SourceProductId;
                    source.SourceUrl = item.SourceUrl; source.SourceBrand = item.RoasterName;
                    source.SellerName = item.SellerName ?? item.RoasterName;
                    source.LastSeenAtUtc = collectedAt; source.IsPresent = true;
                    source.RawJson = JsonSerializer.Serialize(item, WireJson);
                    var warnings = item.ReviewWarnings.ToList();
                    source.Product.ApplyContent(data, fromImport: true);
                    source.Product.ApplyClassification(CoffeeClassificationNormalizer.ActiveOnly(
                        CoffeeClassificationNormalizer.Normalize(item), activeClassification), fromImport: true);
                    var countryCodes = new List<string>();
                    foreach (var origin in item.Origins)
                    {
                        if (!knownCountries.Contains(origin.CountryCode)) warnings.Add("country_not_in_catalog:" + origin.CountryCode);
                        else if (origin.EvidenceField is null || origin.EvidenceField.Contains("name", StringComparison.OrdinalIgnoreCase))
                            warnings.Add("country_requires_confirmation:" + origin.CountryCode);
                        else countryCodes.Add(origin.CountryCode);
                    }
                    if (!source.Product.IsProtected(nameof(CoffeeProduct.Countries)) && countryCodes.Count > 0)
                        source.Product.ReplaceCountries(countryCodes);
                    var seenVariants = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var offer in item.Variants)
                    {
                        seenVariants.Add(offer.ExternalVariantId);
                        var row = source.Variants.SingleOrDefault(v => v.ExternalVariantId == offer.ExternalVariantId);
                        if (row is null)
                        {
                            row = new CoffeeVariant { SourceKey = request.SourceKey, ExternalVariantId = offer.ExternalVariantId };
                            source.Variants.Add(row);
                        }
                        row.WeightGrams = offer.WeightGrams; row.Price = offer.Price; row.Currency = offer.Currency;
                        row.Availability = offer.AvailableForPurchase ? CoffeeAvailability.InStock : CoffeeAvailability.OutOfStock;
                        row.AvailabilityScope = item.AvailabilityScope; row.CheckedAtUtc = collectedAt;
                        row.SourceAttributesJson = offer.SourceAttributes.ValueKind == JsonValueKind.Undefined ? "{}" : offer.SourceAttributes.GetRawText();
                        row.Grind = Attribute(offer.SourceAttributes, "grind");
                        row.RoastPurpose = Attribute(offer.SourceAttributes, "roastPurpose");
                        var brewCode = CoffeeClassificationNormalizer.NormalizeBrewPurpose(row.RoastPurpose);
                        if (brewCode is not null && activeClassification.Any(v => v.GroupCode == "brew" && v.Code == brewCode))
                            row.SetBrewPurpose(brewCode, false, fromImport: true);
                    }
                    foreach (var missing in source.Variants.Where(v => !seenVariants.Contains(v.ExternalVariantId)))
                        missing.Availability = CoffeeAvailability.Unknown;
                    if (item.StoredPhotos.Length > 0 && !source.Product.IsProtected("Photos"))
                    {
                        var keys = item.StoredPhotos.Select(p => p.StorageKey).ToHashSet(StringComparer.Ordinal);
                        db.Set<CoffeeProductPhoto>().RemoveRange(source.Photos.Where(p => !keys.Contains(p.StorageKey)));
                        var index = 0;
                        foreach (var image in item.StoredPhotos.DistinctBy(p => p.StorageKey))
                        {
                            var row = source.Photos.SingleOrDefault(p => p.StorageKey == image.StorageKey);
                            if (row is null) { row = new CoffeeProductPhoto(); source.Photos.Add(row); }
                            row.SourceUrl = image.SourceUrl!; row.StorageKey = image.StorageKey;
                            row.MediaPhotoId = image.Id; row.FileName = image.FileName; row.ContentType = image.ContentType;
                            row.SizeBytes = image.SizeBytes; row.SortIndex = index++;
                        }
                    }
                    if (item.ImageUrls.Length > 0 && item.StoredPhotos.Length == 0) warnings.Add("photos_not_imported");
                    if (item.ImageUrls.Length == 0 && source.Photos.Count == 0) warnings.Add("photos_not_provided");
                    source.ReviewWarnings = warnings.Distinct().ToArray();
                }

                // ponytail: HTML without an independent counter never retires missing offers automatically;
                // add per-source reconciliation approval if automatic retirement becomes necessary.
                var reconcile = request.Snapshot.Coverage.Length > 0 && request.Snapshot.Coverage.All(c =>
                    c.ExpectedProducts.HasValue && c.ExpectedProducts == c.DiscoveredProducts) &&
                    (last is null || request.Snapshot.Products.Length * 2 >= last.Products);
                if (reconcile)
                    foreach (var missing in sources.Where(s => !observed.Contains(s.Id)))
                    { missing.IsPresent = false; foreach (var v in missing.Variants) v.Availability = CoffeeAvailability.Unknown; }
                // Explicit unavailable rows are safe even when an HTML catalogue has no total counter.
                foreach (var excluded in request.Snapshot.Excluded.Where(e => e.Reason == "not_purchasable"))
                {
                    var source = sources.SingleOrDefault(s => s.ExternalProductId == excluded.SourceProductId);
                    if (source is null) continue;
                    source.LastSeenAtUtc = collectedAt;
                    source.IsPresent = true;
                    foreach (var v in source.Variants) { v.Availability = CoffeeAvailability.OutOfStock; v.CheckedAtUtc = collectedAt; }
                }
                var run = previous ?? new CoffeeImportRun { SourceKey = request.SourceKey, SnapshotId = request.SnapshotId, PayloadHash = payloadHash };
                run.CollectedAtUtc = collectedAt; run.AppliedAtUtc = DateTime.UtcNow; run.Status = "Applied"; run.Error = null;
                run.Products = request.Snapshot.Products.Length; run.Variants = request.Snapshot.Products.Sum(p => p.Variants.Length);
                run.Added = added; run.MissingAvailabilityApplied = reconcile;
                if (previous is null) db.CoffeeImportRuns.Add(run);
                await db.SaveChangesAsync(ct);
                return Result(run);
            }, ct);
            return db.Database.CurrentTransaction is null
                ? await db.Database.CreateExecutionStrategy().ExecuteAsync(operation)
                : await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            try
            {
                var failed = await db.CoffeeImportRuns.SingleOrDefaultAsync(r => r.SourceKey == request.SourceKey && r.SnapshotId == request.SnapshotId, ct);
                if (failed is null)
                {
                    db.CoffeeImportRuns.Add(new CoffeeImportRun { SourceKey = request.SourceKey, SnapshotId = request.SnapshotId,
                        PayloadHash = payloadHash, CollectedAtUtc = collectedAt, AppliedAtUtc = DateTime.UtcNow,
                        Status = "Failed", Error = ex.Message[..Math.Min(ex.Message.Length, 2000)] });
                    await db.SaveChangesAsync(ct);
                }
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
            snapshot.Products.Length is < 1 or > 2000 || snapshot.Coverage is null || snapshot.Excluded is null ||
            snapshot.CollectedAtUtc == default || snapshot.CollectedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new CoffeeCatalogException("Incomplete, empty, future-dated or unsupported snapshot.");
        ValidateSourceUrl(snapshot.Source, request.SourceKey);
        if (snapshot.Coverage.Any(c => c is null || c.DiscoveredProducts < 0 || c.ExpectedProducts < 0) ||
            snapshot.Excluded.Any(e => e is null || e.SourceProductId?.Length > 300 ||
                (e.Reason == "not_purchasable" && string.IsNullOrWhiteSpace(e.SourceProductId))))
            throw new CoffeeCatalogException("Invalid coverage or excluded product identity.");
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
    private static CoffeeContent SnapshotContent(CoffeeSnapshotProduct p) => ToContent(new(p.Name, p.Description,
        p.ProductKind, p.ProductForm, p.CompositionKind, p.Processing, p.RoastLevel, p.Acidity, p.Body,
        p.QGraderScoreRaw, p.TasteDescriptors, p.BrewRecommendations, p.GrindOptions, p.Features));
    private static string? Attribute(JsonElement attrs, string key) => attrs.ValueKind == JsonValueKind.Object &&
        attrs.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static CoffeeImportResult Result(CoffeeImportRun r) => new(r.Id, r.SourceKey, r.SnapshotId,
        r.Status, r.Products, r.Variants, r.Added, r.MissingAvailabilityApplied, r.AppliedAtUtc);
}
