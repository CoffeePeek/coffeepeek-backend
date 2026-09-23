using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public partial class ModerationReview : Entity<Guid>
{
    public string? Header { get; private set; }
    public string Comment { get; private set; }
    public Guid UserId { get; private set; }
    public string UserName { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid? ModerationShopId { get; private set; }

    public Rating Rating { get; private set; }

    public string? RejectedReason { get; private set; }
    public Guid? ModeratedBy { get; private set; }
    public DateTime? ModeratedAt { get; private set; }
    public ModerationStatus ModerationStatus { get; private set; }

    public bool IsSoftDelete { get; private set; }
    
    private readonly List<PhotoMetadata> _reviewPhotos = [];
    public IReadOnlyCollection<PhotoMetadata> ReviewPhotos => _reviewPhotos.AsReadOnly();
    
    public ModerationShop? ModerationShop { get; private set; }
    

    // ReSharper disable once UnusedMember.Local
    private ModerationReview()
    {
    }

    internal ModerationReview(Guid userId, Guid shopId, Guid? moderationShopId, string userName, string? header, string comment,
        Rating rating, List<PhotoMetadata> photos)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ShopId = shopId;
        ModerationShopId = moderationShopId is null || moderationShopId == Guid.Empty
            ? null
            : moderationShopId;
        UserName = userName;
        Header = header;
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
