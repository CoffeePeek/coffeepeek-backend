using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductSource : Entity<Guid>
{
    public Guid CoffeeProductId { get; set; }
    public CoffeeProduct Product { get; set; } = null!;
    public string SourceKey { get; set; } = null!;
    public string ExternalProductId { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public string? SellerName { get; set; }
    public string? SourceBrand { get; set; }
    public string RawJson { get; set; } = "{}";
    public string[] ReviewWarnings { get; set; } = [];
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsPresent { get; set; } = true;
    public List<CoffeeVariant> Variants { get; set; } = [];
    public List<CoffeeProductPhoto> Photos { get; set; } = [];
    public CoffeeProductSource() { Id = Guid.NewGuid(); }
}
