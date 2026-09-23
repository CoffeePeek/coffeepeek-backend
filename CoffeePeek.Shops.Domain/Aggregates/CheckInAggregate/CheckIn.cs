using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed partial class CheckIn : AggregateRoot<Guid>
{
    public string? Note { get; private set; }

    public Guid UserId { get; private set; }
    public Guid ShopId { get; private set; }
    public DateTime VisitedAt { get; private set; }
    public Guid? ReviewId { get; private set; }
    
    public Rating Rating { get; private set; }
    
    private readonly List<ShopPhoto> _shopPhotos = [];
    public IReadOnlyCollection<ShopPhoto> ShopPhotos => _shopPhotos.AsReadOnly();
    
    // ReSharper disable once UnusedMember.Local
    private CheckIn() { }

    private CheckIn(Guid userId, Guid shopId, DateTime visitedAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ShopId = shopId;
        VisitedAt = visitedAt;
        CreatedAtUtc = DateTime.UtcNow;

        // Rating is an EF-required owned navigation (NOT NULL columns), so every CheckIn
        // needs a non-null placeholder until AssignRating sets the real value for public check-ins.
        Rating = new Rating(0, 0, 0);
    }
}