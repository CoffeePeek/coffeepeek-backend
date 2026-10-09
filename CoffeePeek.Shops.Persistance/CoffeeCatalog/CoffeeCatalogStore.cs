using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed partial class CoffeeCatalogStore(ShopsDbContext db, IOptions<MediaPublicUrlOptions> media,
    IOptions<CoffeeImportOptions> importOptions, IPublicAddressService addresses,
    ILogger<CoffeeCatalogStore> logger) : ICoffeeCatalogStore
{
    public Task<CountryDto[]> CountriesAsync(CancellationToken ct) => db.Countries.AsNoTracking()
        .Where(c => c.IsActive).OrderBy(c => c.NameRu).Select(c => new CountryDto(c.Code, c.NameRu, c.NameEn)).ToArrayAsync(ct);

    public async Task<RoasterTagDto[]> TagsAsync(bool admin, CancellationToken ct) =>
        (await db.RoasterTags.AsNoTracking().Where(t => admin || t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToArrayAsync(ct))
        .Select(Tag).ToArray();
    public async Task<RoasterTagDto> CreateTagAsync(CreateRoasterTagRequest request, CancellationToken ct)
    {
        if (await db.RoasterTags.AnyAsync(t => t.Slug == request.Slug, ct)) throw new CoffeeCatalogException("Tag slug is reserved.", 409);
        var tag = new RoasterTag(request.Slug, request.Name, request.Description, request.SortOrder);
        db.RoasterTags.Add(tag);
        await db.SaveChangesAsync(ct);
        return Tag(tag);
    }
    public async Task<RoasterTagDto?> EditTagAsync(Guid id, RoasterTagEdit request, CancellationToken ct)
    {
        var tag = await db.RoasterTags.FindAsync([id], ct);
        if (tag is null) return null;
        tag.Update(request.Name, request.Description, request.SortOrder, request.IsActive);
        await db.SaveChangesAsync(ct);
        return Tag(tag);
    }
    public async Task SetRoasterTagsAsync(Guid roasterId, Guid[] tagIds, Guid actorId, CancellationToken ct)
    {
        if (tagIds is null || tagIds.Length > 100) throw new CoffeeCatalogException("Invalid tag selection.");
        if (!await db.Roasters.AnyAsync(r => r.Id == roasterId, ct)) throw new CoffeeCatalogException("Roaster not found.", 404);
        var ids = tagIds.Distinct().ToArray();
        if (await db.RoasterTags.CountAsync(t => ids.Contains(t.Id) && t.IsActive, ct) != ids.Length)
            throw new CoffeeCatalogException("Unknown or inactive roaster tag.");
        var existing = await db.RoasterTagAssignments.Where(a => a.RoasterId == roasterId).ToArrayAsync(ct);
        db.RoasterTagAssignments.RemoveRange(existing.Where(a => !ids.Contains(a.TagId)));
        foreach (var id in ids.Except(existing.Select(a => a.TagId)))
            db.RoasterTagAssignments.Add(new RoasterTagAssignment(roasterId, id, actorId));
        await db.SaveChangesAsync(ct);
    }
    public Task<PublicTagDto[]> RoasterTagsAsync(Guid roasterId, CancellationToken ct) =>
        db.RoasterTagAssignments.AsNoTracking().Where(a => a.RoasterId == roasterId && a.Tag.IsActive)
            .OrderBy(a => a.Tag.SortOrder).ThenBy(a => a.Tag.Name)
            .Select(a => new PublicTagDto(a.Tag.Slug, a.Tag.Name, a.Tag.Description, a.Tag.SortOrder)).ToArrayAsync(ct);

    public async Task<RoasterTagDto[]> AdminRoasterTagsAsync(Guid roasterId, CancellationToken ct)
    {
        if (!await db.Roasters.AnyAsync(r => r.Id == roasterId, ct)) throw new CoffeeCatalogException("Roaster not found.", 404);
        return (await db.RoasterTagAssignments.AsNoTracking().Where(a => a.RoasterId == roasterId)
            .Include(a => a.Tag).OrderBy(a => a.Tag.SortOrder).ToArrayAsync(ct)).Select(a => Tag(a.Tag)).ToArray();
    }
    public async Task<CoffeeRoasterOverviewDto> RoasterOverviewAsync(string slug, CancellationToken ct)
    {
        var id = await ResolveRoasterAsync(slug, ct);
        var count = await db.CoffeeProducts.CountAsync(p => p.RoasterId == id && p.Status == CoffeePublicationStatus.Published &&
            p.Sources.Any(s => s.Variants.Any(v => v.Availability == CoffeeAvailability.InStock)), ct);
        return new(await RoasterTagsAsync(id, ct), count);
    }
    public async Task<bool> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var p = await db.CoffeeProducts.FindAsync([id], ct);
        if (p is null) return false;
        p.SetStatus(CoffeePublicationStatus.Archived);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PublicRoasterDto[]> RoastersAsync(string[] tags, CancellationToken ct)
    {
        if (tags is null || tags.Length > 100 || tags.Any(string.IsNullOrWhiteSpace))
            throw new CoffeeCatalogException("Invalid tag filter.");
        var q = db.Roasters.AsNoTracking();
        foreach (var slug in tags.Distinct())
            q = q.Where(r => db.RoasterTagAssignments.Any(a => a.RoasterId == r.Id && a.Tag.Slug == slug && a.Tag.IsActive));
        var rows = await q.Include(r => r.Photos).OrderByDescending(r => db.CoffeeProducts.Count(p => p.RoasterId == r.Id && p.Status == CoffeePublicationStatus.Published))
            .ThenByDescending(r => r.CoffeeShops.Count(s => !s.IsHidden))
            .ThenBy(r => r.Name).ThenBy(r => r.Id).ToArrayAsync(ct);
        if (rows.Length == 0) return [];
        var ids = rows.Select(r => r.Id).ToArray();
        var counts = await RoasterCountsAsync(ids, ct);
        var available = await db.CoffeeProducts.AsNoTracking()
            .Where(p => ids.Contains(p.RoasterId) && p.Status == CoffeePublicationStatus.Published &&
                p.Sources.Any(s => s.Variants.Any(v => v.Availability == CoffeeAvailability.InStock)))
            .GroupBy(p => p.RoasterId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var assignments = await db.RoasterTagAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.RoasterId) && a.Tag.IsActive)
            .OrderBy(a => a.Tag.SortOrder).ThenBy(a => a.Tag.Name)
            .Select(a => new { a.RoasterId, Tag = new PublicTagDto(a.Tag.Slug, a.Tag.Name, a.Tag.Description, a.Tag.SortOrder) })
            .ToArrayAsync(ct);
        var byRoaster = assignments.ToLookup(a => a.RoasterId, a => a.Tag);
        return rows.Select(r =>
        {
            var stats = counts.GetValueOrDefault(r.Id);
            var summary = Roaster(r, stats);
            return new PublicRoasterDto(summary.Address, summary.Name, summary.PhotoUrl, summary.CoverPhoto,
                byRoaster[r.Id].ToArray(), stats.CoffeeShopsCount, stats.CoffeeProductsCount, available.GetValueOrDefault(r.Id));
        }).ToArray();
    }

    public async Task<PublicPage<CoffeeProductDto>> ListAsync(CoffeeCatalogFilter filter, CancellationToken ct)
    {
        ValidatePage(filter.Page, filter.PageSize);
        if (filter.Countries is null || filter.Countries.Length > 30 || filter.Countries.Any(c => c is null) || filter.Search?.Length > 250 || filter.Brew?.Length > 100 || filter.Processing?.Length > 100)
            throw new CoffeeCatalogException("Invalid coffee filter.");
        var countryCodes = filter.Countries.Select(c => c.ToUpperInvariant()).Distinct().ToArray();
        await ValidateCountriesAsync(countryCodes, ct);
        if (filter.ProductKind is not null and not ("roasted_beans" or "green_beans")) throw new CoffeeCatalogException("Unknown coffee kind.");
        if (filter.WeightGrams is <= 0 || filter.MinPrice is < 0 || filter.MaxPrice is < 0 || filter.MinPrice > filter.MaxPrice)
            throw new CoffeeCatalogException("Invalid weight or price filter.");
        if ((filter.MinPrice.HasValue || filter.MaxPrice.HasValue) && filter.Currency is null)
            throw new CoffeeCatalogException("Price filters require a currency.");
        if (filter.Currency is not null and not ("BYN" or "RUB")) throw new CoffeeCatalogException("Unsupported currency.");
        var q = db.CoffeeProducts.AsNoTracking().Where(p => p.Status == CoffeePublicationStatus.Published);
        if (!string.IsNullOrEmpty(filter.RoasterSlug))
        {
            var roasterId = await ResolveRoasterAsync(filter.RoasterSlug, ct);
            q = q.Where(p => p.RoasterId == roasterId);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.ToLowerInvariant(); q = q.Where(p => p.Name.ToLower().Contains(term));
        }
        if (countryCodes.Length > 0) q = q.Where(p => p.Countries.Any(c => countryCodes.Contains(c.CountryCode)));
        if (filter.ProductKind is not null) q = q.Where(p => p.ProductKind == filter.ProductKind);
        if (filter.Processing is not null)
        {
            var term = filter.Processing.ToLowerInvariant(); q = q.Where(p => p.Processing != null && p.Processing.ToLower().Contains(term));
        }
        if (filter.Brew is not null)
        {
            var term = filter.Brew.ToLowerInvariant(); q = q.Where(p => p.BrewRecommendations.Any(b => b.ToLower().Contains(term)));
        }
        // All offer filters must match ONE variant, not separate bags of the same coffee.
        if (filter.AvailableOnly || filter.WeightGrams.HasValue || filter.Currency is not null || filter.MinPrice.HasValue || filter.MaxPrice.HasValue)
            q = q.Where(p => p.Sources.Any(s => s.Variants.Any(v =>
                (!filter.AvailableOnly || v.Availability == CoffeeAvailability.InStock) &&
                (!filter.WeightGrams.HasValue || v.WeightGrams == filter.WeightGrams) &&
                (filter.Currency == null || v.Currency == filter.Currency) &&
                (!filter.MinPrice.HasValue || v.Price >= filter.MinPrice) &&
                (!filter.MaxPrice.HasValue || v.Price <= filter.MaxPrice))));
        var count = await q.CountAsync(ct);
        // shortcut: Unicode regex classes use the database locale, revisit if deployed with the C locale.
        var rows = await IncludeDetails(q).OrderBy(p => Regex.IsMatch(p.Name, @"^\W|^_")).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArrayAsync(ct);
        return Page(await PublicAsync(rows, ct), count, filter.Page, filter.PageSize);
    }

    public async Task<CoffeeProductDto?> GetAsync(string slug, CancellationToken ct)
    {
        Guid? id = null;
        if (db.PublicAddressesEnabled) id = (await addresses.ResolveAsync(PublicAddressKind.CoffeeProduct, slug, ct))?.EntityId;
        else
        {
            if (!PublicSlug.IsValid(slug)) throw new CoffeeCatalogException("Invalid coffee slug.");
            id = await db.CoffeeProducts.Where(p => p.PublicSlug == slug).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        }
        if (!id.HasValue) return null;
        var row = await IncludeDetails(db.CoffeeProducts.AsNoTracking()).SingleOrDefaultAsync(p => p.Id == id && p.Status == CoffeePublicationStatus.Published, ct);
        return row is null ? null : (await PublicAsync([row], ct))[0];
    }
    public async Task<PublicPage<AdminCoffeeDto>> AdminListAsync(int page, int pageSize, string? status, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var q = db.CoffeeProducts.AsNoTracking();
        if (status is not null)
        {
            if (!Enum.TryParse<CoffeePublicationStatus>(status, true, out var value) || !Enum.IsDefined(value)) throw new CoffeeCatalogException("Unknown status.");
            q = q.Where(p => p.Status == value);
        }
        var count = await q.CountAsync(ct);
        var rows = await IncludeDetails(q).OrderBy(p => Regex.IsMatch(p.Name, @"^\W|^_")).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return Page(rows.Select(Admin).ToArray(), count, page, pageSize);
    }
    public async Task<AdminCoffeeDto?> AdminGetAsync(Guid id, CancellationToken ct)
    {
        var row = await IncludeDetails(db.CoffeeProducts.AsNoTracking()).SingleOrDefaultAsync(p => p.Id == id, ct);
        return row is null ? null : Admin(row);
    }
    public async Task<AdminCoffeeDto> CreateAsync(CreateCoffeeRequest request, CancellationToken ct)
    {
        if (!await db.Roasters.AnyAsync(r => r.Id == request.RoasterId, ct)) throw new CoffeeCatalogException("Roaster not found.", 404);
        await ValidateCountriesAsync(request.CountryCodes, ct);
        var p = new CoffeeProduct(request.RoasterId, ToContent(request.Content));
        p.SetPublicSlug(PublicSlug.Candidate(p.Name, p.Id, 1));
        p.ReplaceCountries(request.CountryCodes);
        db.CoffeeProducts.Add(p);
        await db.SaveChangesAsync(ct);
        return (await AdminGetAsync(p.Id, ct))!;
    }
    public async Task<AdminCoffeeDto?> EditAsync(Guid id, CoffeeEditRequest request, CancellationToken ct)
    {
        var p = await IncludeDetails(db.CoffeeProducts).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (p is null) return null;
        if (request.Version != p.Version) throw new CoffeeCatalogException("Coffee changed; reload before saving.", 409);
        await ValidateCountriesAsync(request.CountryCodes, ct);
        if (request.ProtectedFields is null || !Enum.TryParse<CoffeePublicationStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
            throw new CoffeeCatalogException("Invalid status or protected fields.");
        p.ApplyContent(ToContent(request.Content)); p.ProtectFields(request.ProtectedFields);
        p.ReplaceCountries(request.CountryCodes); p.SetStatus(status);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CoffeeCatalogException("Coffee changed; reload before saving.", 409); }
        return await AdminGetAsync(id, ct);
    }
    public Task<CoffeeImportRunDto[]> RunsAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new CoffeeCatalogException("Limit must be 1–100.");
        return db.CoffeeImportRuns.AsNoTracking().OrderByDescending(r => r.AppliedAtUtc).Take(limit)
            .Select(r => new CoffeeImportRunDto(r.Id, r.SourceKey, r.SnapshotId, r.Status, r.CollectedAtUtc,
                r.AppliedAtUtc, r.Products, r.Variants, r.Added, r.MissingAvailabilityApplied, r.Error)).ToArrayAsync(ct);
    }

    private async Task ValidateCountriesAsync(string[] codes, CancellationToken ct)
    {
        if (codes is null || codes.Length > 30 || await db.Countries.CountAsync(c => codes.Contains(c.Code), ct) != codes.Distinct().Count())
            throw new CoffeeCatalogException("Unknown coffee origin country.");
    }
    private async Task<Guid> ResolveRoasterAsync(string slug, CancellationToken ct)
    {
        if (db.PublicAddressesEnabled)
            return (await addresses.ResolveAsync(PublicAddressKind.Roaster, slug, ct))?.EntityId ?? Guid.Empty;
        return await db.Roasters.Where(r => r.PublicSlug == slug).Select(r => r.Id).SingleOrDefaultAsync(ct);
    }
    private static void ValidatePage(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw new CoffeeCatalogException("Invalid pagination."); }
    private static PublicPage<T> Page<T>(T[] rows, int count, int page, int size) => new(rows, count, (count + size - 1) / size, page, size);
    private static IQueryable<CoffeeProduct> IncludeDetails(IQueryable<CoffeeProduct> q) => q
        .Include(p => p.Countries).ThenInclude(c => c.Country)
        .Include(p => p.Sources).ThenInclude(s => s.Variants)
        .Include(p => p.Sources).ThenInclude(s => s.Photos).AsSplitQuery();
    private static RoasterTagDto Tag(RoasterTag t) => new(t.Id, t.Slug, t.Name, t.Description, t.SortOrder, t.IsActive);
    private static CoffeeContentDto Content(CoffeeProduct p) => new(p.Name, p.Description, p.ProductKind,
        p.ProductForm, p.CompositionKind, p.Processing, p.RoastLevel, p.Acidity, p.Body, p.QGraderScoreRaw,
        p.TasteDescriptors, p.BrewRecommendations, p.GrindOptions, p.Features);
    private static CoffeeContent ToContent(CoffeeContentDto d)
    {
        if (d is null || new[] { d.TasteDescriptors, d.BrewRecommendations, d.GrindOptions, d.Features }
            .Any(a => a is null || a.Length > 100 || a.Any(s => s is null || s.Length > 500)))
            throw new CoffeeCatalogException("Invalid coffee content.");
        if (new[] { d.Processing, d.RoastLevel, d.Acidity, d.Body, d.QGraderScoreRaw }.Any(s => s?.Length > 1000))
            throw new CoffeeCatalogException("Coffee characteristic is too long.");
        return new(d.Name, d.Description, d.ProductKind, d.ProductForm, d.CompositionKind, d.Processing,
            d.RoastLevel, d.Acidity, d.Body, d.QGraderScoreRaw, d.TasteDescriptors, d.BrewRecommendations, d.GrindOptions, d.Features);
    }
    private ShortPhotoMetadataDto[] Photos(CoffeeProduct p) => p.Sources.OrderBy(s => s.SourceKey)
        .SelectMany(s => s.Photos.OrderBy(p => p.SortIndex)).DistinctBy(p => p.StorageKey)
        .Select((p, i) => new ShortPhotoMetadataDto
        {
            Id = p.Id, FileName = p.FileName, StorageKey = p.StorageKey, SortIndex = i, IsPrimary = i == 0,
            FullUrl = MediaStorageUrlBuilder.BuildPublicUrl(media.Value.PublicEndpoint, media.Value.ShopBucketName, p.StorageKey) ?? string.Empty,
            Urls = PhotoUrlsDto.Create(media.Value.PublicEndpoint, media.Value.ImageProxyEndpoint, media.Value.ShopBucketName, p.StorageKey)
        }).ToArray();
    private static CoffeeVariantDto[] Variants(CoffeeProduct p) => p.Sources.OrderBy(s => s.SourceKey)
        .SelectMany(s => s.Variants.OrderBy(v => v.WeightGrams).ThenBy(v => v.Id)
            .Select(v => new CoffeeVariantDto(v.WeightGrams, v.Price, v.Currency, v.Grind, v.RoastPurpose,
                v.Availability.ToString(), v.AvailabilityScope, s.SellerName ?? s.SourceBrand ?? s.SourceKey, s.SourceUrl, v.CheckedAtUtc))).ToArray();
    private static CountryDto[] CountryDtos(CoffeeProduct p) => p.Countries.OrderBy(c => c.Country.NameRu)
        .Select(c => new CountryDto(c.CountryCode, c.Country.NameRu, c.Country.NameEn)).ToArray();
    private AdminCoffeeDto Admin(CoffeeProduct p) => new(p.Id, p.RoasterId, p.PublicSlug, p.Status.ToString(),
        Content(p), CountryDtos(p), p.ProtectedFields, Photos(p), Variants(p), p.Sources.SelectMany(s => s.ReviewWarnings).Distinct().ToArray(), p.Version);
    private async Task<CoffeeProductDto[]> PublicAsync(CoffeeProduct[] products, CancellationToken ct)
    {
        var ids = products.Select(p => p.RoasterId).Distinct().ToArray();
        var roasters = await db.Roasters.AsNoTracking().Include(r => r.Photos).Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var counts = await RoasterCountsAsync(ids, ct);
        return products.Select(p => new CoffeeProductDto(new(p.PublicSlug!, $"/coffees/{p.PublicSlug}", p.SlugRevision),
            Roaster(roasters[p.RoasterId], counts.GetValueOrDefault(p.RoasterId)), Content(p), CountryDtos(p), Photos(p), Variants(p),
            p.Sources.Select(s => (DateTime?)s.LastSeenAtUtc).DefaultIfEmpty().Max())).ToArray();
    }
    private PublicRoasterSummaryDto Roaster(Roaster r, (int CoffeeShopsCount, int CoffeeProductsCount) counts)
    {
        var p = r.Photos.OrderBy(p => p.SortIndex).FirstOrDefault();
        var url = MediaStorageUrlBuilder.BuildPublicUrl(media.Value.PublicEndpoint, media.Value.ShopBucketName, p?.StorageKey);
        var photo = p is null ? null : new ShortPhotoMetadataDto
        { Id = p.Id, FileName = p.FileName, StorageKey = p.StorageKey, FullUrl = url ?? string.Empty, IsPrimary = true,
            Urls = PhotoUrlsDto.Create(media.Value.PublicEndpoint, media.Value.ImageProxyEndpoint, media.Value.ShopBucketName, p.StorageKey) };
        return new(r.PublicSlug is null ? null : new(r.PublicSlug, $"/roasters/{r.PublicSlug}", r.SlugRevision), r.Name, url, photo)
        { CoffeeShopsCount = counts.CoffeeShopsCount, CoffeeProductsCount = counts.CoffeeProductsCount };
    }
}
