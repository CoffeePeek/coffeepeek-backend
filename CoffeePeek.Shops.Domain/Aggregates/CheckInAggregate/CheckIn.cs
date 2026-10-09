using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed partial class CheckIn : AggregateRoot<Guid>
{
    public string? DrinkSlug { get; private set; }
    public string? CustomDrinkName { get; private set; }
    public string? DrinkNameRu { get; private set; }
    public string? DrinkNameEn { get; private set; }

    public void SetDrink(CoffeePeek.Shared.Kernel.Drinks.DrinkSelection? drink)
    {
        DrinkSlug = drink?.Slug;
        CustomDrinkName = drink?.CustomName;
        DrinkNameRu = drink?.NameRu;
        DrinkNameEn = drink?.NameEn;
    }

    public string Note { get; private set; }

    public Guid UserId { get; private set; }
    public Guid ShopId { get; private set; }
    public DateTime VisitedAt { get; private set; }
    public string Username { get; private set; } = "";
    public CheckInVisibility Visibility { get; private set; }
    public int ContentRevision { get; private set; } = 1;
    public CheckInModerationState ModerationState { get; private set; }
    public int? ApprovedRevision { get; private set; }
    public Guid? ModerationSubmissionId { get; private set; }
    public long ModerationDecisionSequence { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public int HelpfulCount { get; private set; }
    public DateTime? FirstPublishedAtUtc { get; private set; }
    public Guid RowVersion { get; private set; } = Guid.NewGuid();

    public bool IsPubliclyVisible => DeletedAtUtc == null && Visibility == CheckInVisibility.Public
        && ModerationState == CheckInModerationState.Approved
        && ApprovedRevision == ContentRevision;

    public Rating? Rating { get; private set; }

    private readonly List<CheckInPhoto> _shopPhotos = [];
    public IReadOnlyCollection<CheckInPhoto> ShopPhotos => _shopPhotos.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private CheckIn() { }

    private CheckIn(Guid userId, Guid shopId, DateTime visitedAt, string? text, Rating? rating, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        UserId = userId;
        ShopId = shopId;
        VisitedAt = visitedAt;
        CreatedAtUtc = DateTime.UtcNow;

        Note = text?.Trim() ?? "";
        Rating = rating;
    }
}
