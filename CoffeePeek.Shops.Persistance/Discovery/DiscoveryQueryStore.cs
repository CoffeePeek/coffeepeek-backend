using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DAddress = CoffeePeek.Contract.Dtos.Discovery.PublicAddress;
using DPhoto = CoffeePeek.Contract.Dtos.Discovery.Photo;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore(ShopsDbContext db, IPublicAddressService addresses,
    IOptions<MediaPublicUrlOptions> media) : IDiscoveryQueryStore
{
    private static readonly string[] Groups = ["brew", "caffeine", "roast", "acidity", "processing", "fermentation", "taste", "composition"];
    private static readonly Dictionary<string, string> GroupNames = new()
    {
        ["brew"] = "Назначение обжарки", ["caffeine"] = "Кофеин", ["roast"] = "Обжарка",
        ["acidity"] = "Кислотность", ["processing"] = "Обработка", ["fermentation"] = "Ферментация",
        ["taste"] = "Вкусовой профиль", ["composition"] = "Состав"
    };

    private sealed record CatalogState(Dictionary<string, HashSet<string>> Active,
        CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeFilterValue[] Values);

    private async Task<CatalogState> CatalogAsync(CancellationToken ct)
    {
        var values = await db.CoffeeFilterValues.AsNoTracking().Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Code).ToArrayAsync(ct);
        return new(Groups.ToDictionary(g => g, g => values.Where(v => v.GroupCode == g).Select(v => v.Code).ToHashSet()), values);
    }

    private static DiscoveryException Invalid(string field, string message) =>
        new(message, errors: new() { [field] = [message] });

    private async Task<Guid[]> ResolveFiltersAsync(PublicAddressKind kind, string[] slugs, string field, CancellationToken ct)
    {
        if (slugs.Length == 0) return [];
        var ids = new List<Guid>();
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            Guid? id;
            if (db.PublicAddressesEnabled)
            {
                try { id = (await addresses.ResolveAsync(kind, slug, ct))?.EntityId; }
                catch (PublicAddressException) { throw Invalid(field, "Unknown public slug."); }
            }
            else id = kind switch
            {
                PublicAddressKind.Roaster => await db.Roasters.Where(x => x.PublicSlug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                PublicAddressKind.City => await db.Cities.Where(x => x.PublicSlug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                PublicAddressKind.Equipment => await db.Equipments.Where(x => x.PublicSlug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                PublicAddressKind.CoffeeBean => await db.CoffeeBeans.Where(x => x.PublicSlug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                PublicAddressKind.BrewMethod => await db.BrewMethods.Where(x => x.PublicSlug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                PublicAddressKind.ShopTag => await db.ShopTags.Where(x => (x.PublicSlug == slug || x.Slug == slug) && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
                _ => null
            };
            if (id is null) throw Invalid(field, "Unknown or inactive public slug.");
            if (kind == PublicAddressKind.ShopTag && !await db.ShopTags.AnyAsync(x => x.Id == id && x.IsActive, ct))
                throw Invalid(field, "Unknown or inactive shop tag.");
            ids.Add(id.Value);
        }
        return ids.Distinct().ToArray();
    }

    private async Task<Guid[]> ValidateCoffeeAsync(CoffeeFilters f, CatalogState catalog, string prefix, CancellationToken ct)
    {
        foreach (var group in Groups)
        {
            if (Selection(f, group).Any(code => !catalog.Active[group].Contains(code)))
                throw Invalid($"{prefix}.{group}", "Unknown or inactive filter code.");
        }
        if (f.Countries.Length > 0 && await db.Countries.CountAsync(c => c.IsActive && f.Countries.Contains(c.Code), ct) != f.Countries.Distinct().Count())
            throw Invalid($"{prefix}.countries", "Unknown or inactive origin country.");
        return await ResolveFiltersAsync(PublicAddressKind.Roaster, f.Roasters, $"{prefix}.roasters", ct);
    }

    private async Task ValidateTagsAsync(RoasterFilters f, CancellationToken ct)
    {
        var requested = f.Tags.Concat(f.ExcludeTags).Distinct().ToArray();
        if (requested.Length > 0 && await db.RoasterTags.CountAsync(t => t.IsActive && requested.Contains(t.Slug), ct) != requested.Length)
            throw Invalid("filters.tags", "Unknown or inactive roaster tag.");
    }

    private static string[] Selection(CoffeeFilters f, string group) => group switch
    {
        "brew" => f.Brew, "caffeine" => f.Caffeine, "roast" => f.Roast, "acidity" => f.Acidity,
        "processing" => f.Processing, "fermentation" => f.Fermentation, "taste" => f.Taste, "composition" => f.Composition,
        _ => []
    };

    private static DAddress Address(string slug, string path, long revision, bool alias = false) =>
        new() { Slug = slug, CanonicalPath = $"/{path}/{slug}", Revision = revision, IsAlias = alias };

    private DPhoto? Photo(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var url = MediaStorageUrlBuilder.BuildPublicUrl(media.Value.PublicEndpoint, media.Value.ShopBucketName, key);
        var urls = CoffeePeek.Contract.Dtos.PhotoUrlsDto.Create(media.Value.PublicEndpoint,
            media.Value.ImageProxyEndpoint, media.Value.ShopBucketName, key);
        return new() { FullUrl = url ?? string.Empty, Urls = urls };
    }

    private static string OfferKey(Guid id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"coffee-offer-v1:{id:N}")))[..32];

    private CoffeeClassification Classification(CoffeeProduct p, CatalogState c) => new()
    {
        DefaultBrewPurposes = p.DefaultBrewPurposes.Where(c.Active["brew"].Contains).ToArray(),
        Caffeine = PublicCode(p.CaffeineCode, c.Active["caffeine"]), RoastLevel = PublicCode(p.RoastLevelCode, c.Active["roast"]),
        Acidity = PublicCode(p.AcidityCode, c.Active["acidity"]), Composition = PublicCode(p.CompositionCode, c.Active["composition"]),
        Processing = p.ProcessingCodes.Where(c.Active["processing"].Contains).ToArray(),
        Fermentation = p.FermentationCodes.Where(c.Active["fermentation"].Contains).ToArray(),
        TasteGroups = p.TasteGroupCodes.Where(c.Active["taste"].Contains).ToArray()
    };

    private static string? PublicCode(string? code, HashSet<string> active) => code is not null && active.Contains(code) ? code : null;
    private CoffeeOffer Offer(CoffeeVariant v, CatalogState c) => new()
    {
        OfferKey = OfferKey(v.Id), WeightGrams = v.WeightGrams, Price = v.Price, Currency = v.Currency,
        BrewPurpose = PublicCode(v.BrewPurposeCode, c.Active["brew"]), Grind = v.Grind,
        Availability = v.Availability.ToString(), AvailabilityScope = v.AvailabilityScope,
        SellerName = v.Source.SellerName ?? v.Source.SourceBrand ?? v.Source.SourceKey,
        SourceUrl = v.Source.SourceUrl, CheckedAtUtc = v.CheckedAtUtc
    };

    private RoasterIdentity Identity(Roaster r) => new()
    { Address = Address(r.PublicSlug!, "roasters", r.SlugRevision), Name = r.Name, CoverPhoto = Photo(r.Photos.OrderBy(p => p.SortIndex).FirstOrDefault()?.StorageKey) };

    private static IQueryable<CoffeeProduct> CardDetails(IQueryable<CoffeeProduct> q) => q
        .Include(p => p.Countries).ThenInclude(c => c.Country)
        .Include(p => p.Sources).ThenInclude(s => s.Photos).AsSplitQuery();

    private static IQueryable<CoffeeProduct> Details(IQueryable<CoffeeProduct> q) => CardDetails(q)
        .Include(p => p.Sources).ThenInclude(s => s.Variants);

    private static CoffeePeek.Contract.Dtos.Discovery.Country[] Countries(CoffeeProduct p) => p.Countries.Where(c => c.Country.IsActive)
        .OrderBy(c => c.Country.NameRu).Select(c => new CoffeePeek.Contract.Dtos.Discovery.Country { Code = c.CountryCode, NameRu = c.Country.NameRu, NameEn = c.Country.NameEn }).ToArray();
    private DPhoto[] Photos(CoffeeProduct p) => p.Sources.OrderBy(s => s.SourceKey).SelectMany(s => s.Photos.OrderBy(p => p.SortIndex))
        .DistinctBy(p => p.StorageKey).Select(p => Photo(p.StorageKey)!).ToArray();

    public async Task<CoffeeDetails?> CoffeeDetailsAsync(string slug, CancellationToken ct)
    {
        if (!CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.IsValid(slug)) return null;
        Guid? id = null;
        var alias = false;
        if (db.PublicAddressesEnabled)
        {
            var a = await addresses.ResolveAsync(PublicAddressKind.CoffeeProduct, slug, ct);
            id = a?.EntityId; alias = a?.IsAlias == true;
        }
        else id = await db.CoffeeProducts.Where(p => p.PublicSlug == slug).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        if (id is null) return null;
        var p = await Details(db.CoffeeProducts.AsNoTracking()).SingleOrDefaultAsync(p => p.Id == id && p.Status == CoffeePublicationStatus.Published, ct);
        if (p is null) return null;
        var r = await db.Roasters.AsNoTracking().Include(r => r.Photos).SingleAsync(r => r.Id == p.RoasterId, ct);
        if (r.PublicSlug is null || p.PublicSlug is null) return null;
        var c = await CatalogAsync(ct);
        return new()
        {
            Address = Address(p.PublicSlug, "coffees", p.SlugRevision, alias), Name = p.Name, Description = p.Description,
            Roaster = Identity(r), ProductKind = p.ProductKind, ProductForm = p.ProductForm, Classification = Classification(p, c),
            TasteDescriptors = p.TasteDescriptors, Countries = Countries(p), Photos = Photos(p),
            Offers = p.Sources.SelectMany(s => s.Variants).OrderBy(v => v.WeightGrams).ThenBy(v => v.Id).Select(v => Offer(v, c)).ToArray(),
            CreatedAtUtc = p.CreatedAtUtc, CatalogCheckedAtUtc = p.Sources.Select(s => (DateTime?)s.LastSeenAtUtc).DefaultIfEmpty().Max()
        };
    }
}
