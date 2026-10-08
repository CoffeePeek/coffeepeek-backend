using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;

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
