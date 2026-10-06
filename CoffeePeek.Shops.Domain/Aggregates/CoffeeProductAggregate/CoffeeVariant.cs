using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeVariant : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; set; }
    public CoffeeProductSource Source { get; set; } = null!;
    public string SourceKey { get; set; } = null!;
    public string ExternalVariantId { get; set; } = null!;
    public int? WeightGrams { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = null!;
    public string? Grind { get; set; }
    public string? RoastPurpose { get; set; }
    public string? BrewPurposeCode { get; private set; }
    public bool BrewPurposeProtected { get; private set; }
    public void SetBrewPurpose(string? code, bool protectFromImport, bool fromImport = false)
    {
        if (code is not null && !CoffeeFilterValue.IsValidCode(code)) throw new DomainException("Invalid brew purpose code.");
        if (fromImport)
        {
            if (!BrewPurposeProtected && code is not null) BrewPurposeCode = code;
            return;
        }
        BrewPurposeCode = code;
        BrewPurposeProtected = protectFromImport;
    }
    public string SourceAttributesJson { get; set; } = "{}";
    public CoffeeAvailability Availability { get; set; }
    public string AvailabilityScope { get; set; } = "online";
    public DateTime CheckedAtUtc { get; set; }
    public CoffeeVariant() { Id = Guid.NewGuid(); }
}
