using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public partial class ModerationRoaster : Entity<Guid>
{
    public string Name { get; private set; } = null!;
    public string? About { get; private set; }
    public ModerationStatus ModerationStatus { get; private set; }
    public string? RejectedReason { get; private set; }

    public Guid? UserId { get; private init; }
    public Guid? CityId { get; private set; }

    public ModerationRoasterContact? Contact { get; private set; }
    public ModerationLocation? Location { get; private set; }

    private readonly List<ModerationRoasterPhoto> _photos = [];
    public IReadOnlyCollection<ModerationRoasterPhoto> Photos => _photos.AsReadOnly();

    private ModerationRoaster() { }
}
