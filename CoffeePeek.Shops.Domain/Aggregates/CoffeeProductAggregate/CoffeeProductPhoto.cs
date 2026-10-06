using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductPhoto : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; set; }
    public string SourceUrl { get; set; } = null!;
    public string StorageKey { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public Guid MediaPhotoId { get; set; }
    public int SortIndex { get; set; }
    public CoffeeProductPhoto() { Id = Guid.NewGuid(); }
}
