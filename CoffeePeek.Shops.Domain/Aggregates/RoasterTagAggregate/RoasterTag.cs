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

public sealed class RoasterTagAssignment
{
    public Guid RoasterId { get; private set; }
    public Guid TagId { get; private set; }
    public RoasterTag Tag { get; private set; } = null!;
    public Guid AssignedByUserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    private RoasterTagAssignment() { }
    public RoasterTagAssignment(Guid roasterId, Guid tagId, Guid actorId)
    { RoasterId = roasterId; TagId = tagId; AssignedByUserId = actorId; AssignedAtUtc = DateTime.UtcNow; }
}
