using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProduct : Entity<Guid>, IPublicSlugOwner
{
    public Guid RoasterId { get; private set; }
    public string? PublicSlug { get; private set; }
    public long SlugRevision { get; private set; }
    public long Version { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string ProductKind { get; private set; } = null!;
    public string ProductForm { get; private set; } = null!;
    public string CompositionKind { get; private set; } = null!;
    public string? Processing { get; private set; }
    public string? RoastLevel { get; private set; }
    public string? Acidity { get; private set; }
    public string? Body { get; private set; }
    public string? QGraderScoreRaw { get; private set; }
    private string[] _tasteDescriptors = [];
    public string[] TasteDescriptors { get => [.. _tasteDescriptors]; private set => _tasteDescriptors = [.. value]; }
    private string[] _brewRecommendations = [];
    public string[] BrewRecommendations { get => [.. _brewRecommendations]; private set => _brewRecommendations = [.. value]; }
    private string[] _grindOptions = [];
    public string[] GrindOptions { get => [.. _grindOptions]; private set => _grindOptions = [.. value]; }
    private string[] _features = [];
    public string[] Features { get => [.. _features]; private set => _features = [.. value]; }
    private string[] _protectedFields = [];
    public string[] ProtectedFields { get => [.. _protectedFields]; private set => _protectedFields = [.. value]; }
    private string[] _defaultBrewPurposes = [];
    public string[] DefaultBrewPurposes { get => [.. _defaultBrewPurposes]; private set => _defaultBrewPurposes = [.. value]; }
    public string? CaffeineCode { get; private set; }
    public string? RoastLevelCode { get; private set; }
    public string? AcidityCode { get; private set; }
    private string[] _processingCodes = [];
    public string[] ProcessingCodes { get => [.. _processingCodes]; private set => _processingCodes = [.. value]; }
    private string[] _fermentationCodes = [];
    public string[] FermentationCodes { get => [.. _fermentationCodes]; private set => _fermentationCodes = [.. value]; }
    private string[] _tasteGroupCodes = [];
    public string[] TasteGroupCodes { get => [.. _tasteGroupCodes]; private set => _tasteGroupCodes = [.. value]; }
    public string? CompositionCode { get; private set; }
    private string[] _classificationProtectedFields = [];
    public string[] ClassificationProtectedFields { get => [.. _classificationProtectedFields]; private set => _classificationProtectedFields = [.. value]; }
    public CoffeePublicationStatus Status { get; private set; }
    private readonly List<CoffeeProductCountry> _countries = [];
    public IReadOnlyCollection<CoffeeProductCountry> Countries => _countries.AsReadOnly();
    private readonly List<CoffeeProductSource> _sources = [];
    public IReadOnlyCollection<CoffeeProductSource> Sources => _sources.AsReadOnly();

    private CoffeeProduct() { }
    public CoffeeProduct(Guid roasterId, CoffeeContent content)
    {
        if (roasterId == Guid.Empty) throw new DomainException("Roaster is required.");
        Id = Guid.NewGuid();
        RoasterId = roasterId;
        ApplyContent(content);
    }

    public void SetPublicSlug(string slug)
    {
        CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Validate(slug);
        if (PublicSlug == slug) return;
        PublicSlug = slug;
        SlugRevision = checked(SlugRevision + 1);
    }

    public bool IsProtected(string field) => ProtectedFields.Contains(field, StringComparer.Ordinal);

    public void ApplyClassification(CoffeeClassificationData data, bool fromImport = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (var values in new[] { data.DefaultBrewPurposes, data.Processing, data.Fermentation, data.TasteGroups })
            if (values is null || values.Length > 30 || values.Any(v => !CoffeeFilterValue.IsValidCode(v)) ||
                values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new DomainException("Invalid coffee classification selection.");
        if (new[] { data.Caffeine, data.RoastLevel, data.Acidity, data.Composition }.Any(v => v is not null && !CoffeeFilterValue.IsValidCode(v)))
            throw new DomainException("Invalid coffee classification code.");
        bool Write(string field) => !fromImport || !ClassificationProtectedFields.Contains(field, StringComparer.Ordinal);
        if (Write("defaultBrewPurposes") && (!fromImport || data.DefaultBrewPurposes.Length > 0)) DefaultBrewPurposes = data.DefaultBrewPurposes.ToArray();
        if (Write("caffeine") && (!fromImport || data.Caffeine is not null)) CaffeineCode = data.Caffeine;
        if (Write("roastLevel") && (!fromImport || data.RoastLevel is not null)) RoastLevelCode = data.RoastLevel;
        if (Write("acidity") && (!fromImport || data.Acidity is not null)) AcidityCode = data.Acidity;
        if (Write("processing") && (!fromImport || data.Processing.Length > 0)) ProcessingCodes = data.Processing.ToArray();
        if (Write("fermentation") && (!fromImport || data.Fermentation.Length > 0)) FermentationCodes = data.Fermentation.ToArray();
        if (Write("tasteGroups") && (!fromImport || data.TasteGroups.Length > 0)) TasteGroupCodes = data.TasteGroups.ToArray();
        if (Write("composition") && (!fromImport || data.Composition is not null)) CompositionCode = data.Composition;
        AdvanceVersion();
    }

    public void ProtectClassificationFields(string[] fields)
    {
        string[] allowed = ["defaultBrewPurposes", "caffeine", "roastLevel", "acidity", "processing", "fermentation", "tasteGroups", "composition"];
        if (fields is null || fields.Any(f => !allowed.Contains(f, StringComparer.Ordinal)) || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new DomainException("Unknown or duplicate protected classification field.");
        ClassificationProtectedFields = fields.ToArray();
        AdvanceVersion();
    }

    public void AdvanceVersion() => Version = checked(Version + 1);
    public void ProtectFields(string[] fields)
    {
        var allowed = new[] { nameof(Name), nameof(Description), nameof(ProductKind), nameof(ProductForm),
            nameof(CompositionKind), nameof(Processing), nameof(RoastLevel), nameof(Acidity), nameof(Body),
            nameof(QGraderScoreRaw), nameof(TasteDescriptors), nameof(BrewRecommendations), nameof(GrindOptions),
            nameof(Features), nameof(Countries), "Photos" };
        if (fields is null || fields.Any(f => !allowed.Contains(f, StringComparer.Ordinal)))
            throw new DomainException("Unknown protected coffee field.");
        ProtectedFields = fields.Distinct(StringComparer.Ordinal).ToArray();
        Version = checked(Version + 1);
    }

    public void ApplyContent(CoffeeContent data, bool fromImport = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 250)
            throw new DomainException("Coffee name must contain 1–250 characters.");
        if (data.Description?.Length > 20000) throw new DomainException("Coffee description is too long.");
        if (data.ProductKind is not ("roasted_beans" or "green_beans") ||
            data.ProductForm is not ("whole_beans" or "ground_only") ||
            data.CompositionKind is not ("unknown" or "single_origin" or "blend"))
            throw new DomainException("Invalid coffee kind, form or composition.");
        if (new[] { data.TasteDescriptors, data.BrewRecommendations, data.GrindOptions, data.Features }
                .Any(values => values is null || values.Length > 100 || values.Any(v => v is null || v.Length > 500)) ||
            new[] { data.Processing, data.RoastLevel, data.Acidity, data.Body, data.QGraderScoreRaw }.Any(v => v?.Length > 1000))
            throw new DomainException("Invalid coffee characteristics.");
        bool Write(string field) => !fromImport || !IsProtected(field);
        if (Write(nameof(Name))) Name = data.Name.Trim();
        if (Write(nameof(Description)) && (!fromImport || data.Description is not null)) Description = data.Description;
        if (Write(nameof(ProductKind))) ProductKind = data.ProductKind;
        if (Write(nameof(ProductForm))) ProductForm = data.ProductForm;
        if (Write(nameof(CompositionKind))) CompositionKind = data.CompositionKind;
        if (Write(nameof(Processing)) && (!fromImport || data.Processing is not null)) Processing = data.Processing;
        if (Write(nameof(RoastLevel)) && (!fromImport || data.RoastLevel is not null)) RoastLevel = data.RoastLevel;
        if (Write(nameof(Acidity)) && (!fromImport || data.Acidity is not null)) Acidity = data.Acidity;
        if (Write(nameof(Body)) && (!fromImport || data.Body is not null)) Body = data.Body;
        if (Write(nameof(QGraderScoreRaw)) && (!fromImport || data.QGraderScoreRaw is not null)) QGraderScoreRaw = data.QGraderScoreRaw;
        if (Write(nameof(TasteDescriptors)) && (!fromImport || data.TasteDescriptors.Length > 0)) TasteDescriptors = data.TasteDescriptors;
        if (Write(nameof(BrewRecommendations)) && (!fromImport || data.BrewRecommendations.Length > 0)) BrewRecommendations = data.BrewRecommendations;
        if (Write(nameof(GrindOptions)) && (!fromImport || data.GrindOptions.Length > 0)) GrindOptions = data.GrindOptions;
        if (Write(nameof(Features)) && (!fromImport || data.Features.Length > 0)) Features = data.Features;
        Version = checked(Version + 1);
    }

    public void SetStatus(CoffeePublicationStatus status)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Invalid publication status.");
        Status = status;
        Version = checked(Version + 1);
    }
    public void ReplaceCountries(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var values = codes.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 30 || values.Any(c => c is null || c.Length != 2 || c.Any(x => x is < 'A' or > 'Z')))
            throw new DomainException("Country codes must be ISO alpha-2 codes.");
        _countries.RemoveAll(c => !values.Contains(c.CountryCode));
        foreach (var code in values.Except(_countries.Select(c => c.CountryCode)))
            _countries.Add(new CoffeeProductCountry(Id, code));
        Version = checked(Version + 1);
    }

    public void ApplyImportedCountries(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var values = codes.ToArray();
        if (values.Length == 0 || IsProtected(nameof(Countries)))
            return;
        ReplaceCountries(values);
    }

    public void AttachSource(CoffeeProductSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CoffeeProductId != Id || !ReferenceEquals(source.Product, this))
            throw new DomainException("Coffee source must belong to this product.");
        if (_sources.Any(existing => existing.Id == source.Id))
            return;
        _sources.Add(source);
        AdvanceVersion();
    }
}
