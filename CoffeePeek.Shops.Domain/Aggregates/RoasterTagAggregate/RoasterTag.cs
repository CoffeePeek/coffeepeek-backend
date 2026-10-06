using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;

public sealed class RoasterTag : Entity<Guid>
{
    public string Slug { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    private RoasterTag() { }
    public RoasterTag(string slug, string name, string? description, int sortOrder)
    {
        Id = Guid.NewGuid();
        if (!PublicSlug.IsValid(slug)) throw new DomainException("Invalid or reserved roaster tag slug.");
        Slug = slug;
        Update(name, description, sortOrder, true);
    }
    public void Update(string name, string? description, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || description?.Length > 2000)
            throw new DomainException("Invalid roaster tag name or description.");
        Name = name.Trim(); Description = description?.Trim(); SortOrder = sortOrder; IsActive = isActive;
    }
}
