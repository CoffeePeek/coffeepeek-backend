using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed class DiscoveryClassificationStore(ShopsDbContext db) : IDiscoveryClassificationStore
{
    private static readonly Dictionary<string, string> GroupNames = new(StringComparer.Ordinal)
    {
        ["brew"] = "Назначение обжарки", ["caffeine"] = "Кофеин", ["roast"] = "Обжарка", ["acidity"] = "Кислотность",
        ["processing"] = "Обработка", ["fermentation"] = "Ферментация", ["taste"] = "Вкусовой профиль", ["composition"] = "Состав"
    };

    public async Task<FilterGroup[]> PublicValuesAsync(CancellationToken ct)
    {
        var values = await db.CoffeeFilterValues.AsNoTracking().Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Name).ThenBy(v => v.Code).ToArrayAsync(ct);
        return CoffeeFilterValue.GroupCodes.Select(group => new FilterGroup
        {
            Code = group, Name = GroupNames[group], Selection = "or",
            Values = values.Where(v => v.GroupCode == group).Select(v => new FilterValue
                { Code = v.Code, Name = v.Name, Description = v.Description, SortOrder = v.SortOrder }).ToArray()
        }).ToArray();
    }

    public async Task<AdminCoffeeFilterValue[]> AdminValuesAsync(CancellationToken ct) =>
        (await db.CoffeeFilterValues.AsNoTracking().OrderBy(v => v.GroupCode).ThenBy(v => v.SortOrder)
            .ThenBy(v => v.Name).ThenBy(v => v.Code).ToArrayAsync(ct)).Select(MapValue).ToArray();

    public async Task<AdminCoffeeFilterValue> CreateValueAsync(CreateCoffeeFilterValueRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await db.CoffeeFilterValues.AnyAsync(v => v.GroupCode == request.GroupCode && v.Code == request.Code, ct))
            throw new DiscoveryException("Coffee filter group/code is reserved.", 409, "CODE_ALREADY_EXISTS");
        var value = new CoffeeFilterValue(request.GroupCode, request.Code, request.Name, request.Description, request.SortOrder);
        db.CoffeeFilterValues.Add(value);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new DiscoveryException("Coffee filter group/code is reserved.", 409, "CODE_ALREADY_EXISTS"); }
        return MapValue(value);
    }

    public async Task<AdminCoffeeFilterValue?> EditValueAsync(Guid id, EditCoffeeFilterValueRequest request, CancellationToken ct)
    {
        var value = await db.CoffeeFilterValues.FindAsync([id], ct);
        if (value is null) return null;
        value.Update(request.Name, request.Description, request.SortOrder, request.IsActive);
        await db.SaveChangesAsync(ct);
        return MapValue(value);
    }

    public async Task<AdminCoffeeFilterValue?> DeactivateValueAsync(Guid id, CancellationToken ct)
    {
        var value = await db.CoffeeFilterValues.FindAsync([id], ct);
        if (value is null) return null;
        value.Update(value.Name, value.Description, value.SortOrder, false);
        await db.SaveChangesAsync(ct);
        return MapValue(value);
    }

    public async Task<AdminCoffeeClassification?> GetClassificationAsync(Guid id, CancellationToken ct)
    {
        var product = await Products().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? null : MapProduct(product);
    }

    public async Task<AdminCoffeeClassification?> EditClassificationAsync(Guid id, CoffeeClassificationEditRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;
        CheckVersion(product, request.Version);
        if (request.Classification is null || request.ProtectedFields is null)
            throw new DiscoveryException("Classification and protected fields are required.");
        var data = request.Classification;
        var domainData = new CoffeeClassificationData(data.DefaultBrewPurposes, data.Caffeine, data.RoastLevel, data.Acidity,
            data.Processing, data.Fermentation, data.TasteGroups, data.Composition);
        await ValidateAssignmentsAsync(domainData, ct);
        // All validation precedes mutation; the separate legacy protection list is left intact.
        string[] protection = ["defaultBrewPurposes", "caffeine", "roastLevel", "acidity", "processing", "fermentation", "tasteGroups", "composition"];
        if (request.ProtectedFields.Any(f => !protection.Contains(f, StringComparer.Ordinal)) ||
            request.ProtectedFields.Distinct(StringComparer.Ordinal).Count() != request.ProtectedFields.Length)
            throw new DiscoveryException("Invalid protected classification fields.");
        product.ApplyClassification(domainData);
        product.ProtectClassificationFields(request.ProtectedFields);
        await SaveClassificationAsync(ct);
        return MapProduct(product);
    }

    public async Task<AdminCoffeeClassification?> EditVariantClassificationAsync(Guid id, Guid variantId, VariantClassificationEditRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;
        var variant = product.Sources.SelectMany(s => s.Variants).SingleOrDefault(v => v.Id == variantId);
        if (variant is null) return null;
        CheckVersion(product, request.Version);
        if (request.BrewPurpose is not null && !await db.CoffeeFilterValues.AnyAsync(v =>
                v.GroupCode == "brew" && v.Code == request.BrewPurpose && v.IsActive, ct))
            throw new DiscoveryException("Unknown or inactive brew purpose.");
        variant.SetBrewPurpose(request.BrewPurpose, request.ProtectFromImport);
        product.AdvanceVersion();
        await SaveClassificationAsync(ct);
        return MapProduct(product);
    }

    private IQueryable<CoffeeProduct> Products() => db.CoffeeProducts.Include(p => p.Sources).ThenInclude(s => s.Variants).AsSplitQuery();

    private static void CheckVersion(CoffeeProduct product, long version)
    {
        if (version < 1 || version != product.Version) throw new DiscoveryException("Coffee changed; reload before saving.", 409, "VERSION_CONFLICT");
    }

    private async Task SaveClassificationAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new DiscoveryException("Coffee changed; reload before saving.", 409, "VERSION_CONFLICT"); }
    }

    private async Task ValidateAssignmentsAsync(CoffeeClassificationData data, CancellationToken ct)
    {
        var assignments = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["brew"] = data.DefaultBrewPurposes, ["caffeine"] = data.Caffeine is null ? [] : [data.Caffeine],
            ["roast"] = data.RoastLevel is null ? [] : [data.RoastLevel], ["acidity"] = data.Acidity is null ? [] : [data.Acidity],
            ["processing"] = data.Processing, ["fermentation"] = data.Fermentation, ["taste"] = data.TasteGroups,
            ["composition"] = data.Composition is null ? [] : [data.Composition]
        };
        var active = await db.CoffeeFilterValues.AsNoTracking().Where(v => v.IsActive).ToArrayAsync(ct);
        foreach (var (group, codes) in assignments)
            if (codes is null || codes.Length > 30 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length ||
                codes.Any(code => !active.Any(v => v.GroupCode == group && v.Code == code)))
                throw new DiscoveryException("Unknown, duplicate or inactive coffee classification value.");
    }

    private static AdminCoffeeFilterValue MapValue(CoffeeFilterValue value) => new()
    {
        Id = value.Id, GroupCode = value.GroupCode, Code = value.Code, Name = value.Name,
        Description = value.Description, SortOrder = value.SortOrder, IsActive = value.IsActive
    };

    private static AdminCoffeeClassification MapProduct(CoffeeProduct product) => new()
    {
        Id = product.Id, Version = product.Version, ProtectedFields = product.ClassificationProtectedFields.ToArray(),
        Classification = new CoffeeClassification
        {
            DefaultBrewPurposes = product.DefaultBrewPurposes.ToArray(), Caffeine = product.CaffeineCode,
            RoastLevel = product.RoastLevelCode, Acidity = product.AcidityCode, Processing = product.ProcessingCodes.ToArray(),
            Fermentation = product.FermentationCodes.ToArray(), TasteGroups = product.TasteGroupCodes.ToArray(), Composition = product.CompositionCode
        },
        Variants = product.Sources.SelectMany(s => s.Variants).OrderBy(v => v.Id).Select(v => new AdminVariantClassification
            { Id = v.Id, BrewPurpose = v.BrewPurposeCode, ProtectFromImport = v.BrewPurposeProtected }).ToArray()
    };
}
