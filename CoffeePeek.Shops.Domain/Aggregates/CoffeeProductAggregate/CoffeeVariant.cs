using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeVariant : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; private set; }
    public CoffeeProductSource Source { get; private set; } = null!;
    public string SourceKey { get; private set; } = null!;
    public string ExternalVariantId { get; private set; } = null!;
    public int? WeightGrams { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? Grind { get; private set; }
    public string? RoastPurpose { get; private set; }
    public string? BrewPurposeCode { get; private set; }
    public bool BrewPurposeProtected { get; private set; }
    public void SetBrewPurpose(string? code, bool protectFromImport, bool fromImport = false)
    {
        if (code is not null && !CoffeeFilterValue.IsValidCode(code)) throw new DomainException("Invalid brew purpose code.");
        if (fromImport)
        {
            if (BrewPurposeProtected || code is null) return;
            protectFromImport = false;
        }
        if (BrewPurposeCode == code && BrewPurposeProtected == protectFromImport) return;
        Source.Product.AdvanceVersion();
        BrewPurposeCode = code;
        BrewPurposeProtected = protectFromImport;
    }
    public string SourceAttributesJson { get; private set; } = "{}";
    public CoffeeAvailability Availability { get; private set; }
    public string AvailabilityScope { get; private set; } = "online";
    public DateTime CheckedAtUtc { get; private set; }
    private CoffeeVariant() { }
    public CoffeeVariant(CoffeeProductSource source, string externalVariantId, int? weightGrams, decimal price,
        string currency, CoffeeAvailability availability = CoffeeAvailability.Unknown, DateTime? checkedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        CoffeeProductSource.ValidateIdentity(externalVariantId, 300);
        var time = checkedAtUtc ?? DateTime.UtcNow;
        ValidateOffer(weightGrams, price, currency, availability, "online", time);
        Id = Guid.NewGuid(); Source = source; CoffeeProductSourceId = source.Id;
        SourceKey = source.SourceKey; ExternalVariantId = externalVariantId;
        WeightGrams = weightGrams; Price = price; Currency = currency; Availability = availability; CheckedAtUtc = time;
    }

    public void UpdateOffer(int? weightGrams, decimal price, string currency, CoffeeAvailability availability,
        string scope, DateTime checkedAtUtc, string? grind = null, string? roastPurpose = null, string attributesJson = "{}")
    {
        ValidateOffer(weightGrams, price, currency, availability, scope, checkedAtUtc);
        using var json = System.Text.Json.JsonDocument.Parse(attributesJson);
        Source.Product.AdvanceVersion();
        WeightGrams = weightGrams; Price = price; Currency = currency; Availability = availability;
        AvailabilityScope = scope; CheckedAtUtc = checkedAtUtc; Grind = grind; RoastPurpose = roastPurpose;
        SourceAttributesJson = attributesJson;
    }

    public void MarkAvailability(CoffeeAvailability availability, DateTime? checkedAtUtc = null)
    {
        if (!Enum.IsDefined(availability) || checkedAtUtc is { } time && (time == default || time.Kind != DateTimeKind.Utc))
            throw new DomainException("Invalid coffee availability observation.");
        Source.Product.AdvanceVersion();
        Availability = availability;
        if (checkedAtUtc.HasValue) CheckedAtUtc = checkedAtUtc.Value;
    }

    private static void ValidateOffer(int? weight, decimal price, string currency, CoffeeAvailability availability,
        string scope, DateTime checkedAt)
    {
        if (weight is <= 0 or > 100000 || price is <= 0 or > 999999999 || decimal.Round(price, 2) != price ||
            currency is not ("BYN" or "RUB") || !Enum.IsDefined(availability) || scope is not ("online" or "in_store") ||
            checkedAt == default || checkedAt.Kind != DateTimeKind.Utc)
            throw new DomainException("Invalid coffee offer.");
    }
}
