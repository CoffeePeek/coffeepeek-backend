using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Drinks;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public partial class ModerationReview : Entity<Guid>
{
    public string Comment { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string UserName { get; private set; } = null!;
    public Guid ShopId { get; private set; }
    public Guid CheckInId { get; private set; }
    public int ContentRevision { get; private set; }
    public string? DrinkSlug { get; private set; }
    public string? CustomDrinkName { get; private set; }
    public string? DrinkNameRu { get; private set; }
    public string? DrinkNameEn { get; private set; }
    public DateTime VisitedAtUtc { get; private set; }
    public long DecisionSequence { get; private set; }

    public Rating? Rating { get; private set; }

    public string? RejectedReason { get; private set; }
    public Guid? ModeratedBy { get; private set; }
    public DateTime? ModeratedAt { get; private set; }
    public ModerationStatus ModerationStatus { get; private set; }

    public bool IsSoftDelete { get; private set; }

    private readonly List<PhotoMetadata> _reviewPhotos = [];
    public IReadOnlyCollection<PhotoMetadata> ReviewPhotos => _reviewPhotos.AsReadOnly();



    // ReSharper disable once UnusedMember.Local
    private ModerationReview()
    {
    }

    private ModerationReview(Guid checkInId, int contentRevision, Guid userId, Guid shopId, string userName,
        string comment, Rating? rating, DrinkSelection? drink, DateTime visitedAtUtc, List<PhotoMetadata> photos)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ShopId = shopId;
        CheckInId = checkInId;
        ContentRevision = contentRevision;
        DrinkSlug = drink?.Slug;
        CustomDrinkName = drink?.CustomName;
        DrinkNameRu = drink?.NameRu;
        DrinkNameEn = drink?.NameEn;
        VisitedAtUtc = visitedAtUtc;
        UserName = userName;
        Comment = comment;
        Rating = rating;
        ModerationStatus = ModerationStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;

        if (photos.Count != 0)
        {
            _reviewPhotos.AddRange(photos);
        }
    }
}
