using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Drinks;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public partial class ModerationReview
{
    public static ModerationReview CreateCheckInSnapshot(Guid checkInId, int contentRevision,
        Guid userId, Guid shopId, string userName, string text, int place, int service, int coffee,
        DrinkSelection? drink, DateTime visitedAtUtc, List<PhotoMetadata> photos)
    {
        if (checkInId == Guid.Empty || userId == Guid.Empty || shopId == Guid.Empty || contentRevision < 1)
            throw new DomainException("A check-in snapshot requires valid identity and revision.");
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > BusinessConstants.MaxCheckInTextLength)
            throw new DomainException($"Check-in text is required and must not exceed {BusinessConstants.MaxCheckInTextLength} characters.");
        if (drink is not null && (string.IsNullOrWhiteSpace(drink.Slug) || drink.Slug.Length > DrinkSelection.MaxSlugLength ||
            string.IsNullOrWhiteSpace(drink.NameRu) || drink.NameRu.Length > DrinkSelection.MaxNameLength ||
            string.IsNullOrWhiteSpace(drink.NameEn) || drink.NameEn.Length > DrinkSelection.MaxNameLength ||
            (drink.Slug == DrinkSelection.OtherSlug
                ? string.IsNullOrWhiteSpace(drink.CustomName) || drink.CustomName.Length > DrinkSelection.MaxNameLength
                : drink.CustomName is not null)))
            throw new DomainException("Invalid frozen check-in drink selection.");
        if (visitedAtUtc == default || visitedAtUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A check-in snapshot requires a UTC visit time.");
        if (photos is null || photos.Any(p => p.CheckInPhotoId is null || p.CheckInPhotoId == Guid.Empty || p.OwnerId != userId))
            throw new DomainException("Snapshot photos require canonical identities and the check-in author.");

        return new ModerationReview(checkInId, contentRevision, userId, shopId, userName, text.Trim(),
            Rating.Create(place, service, coffee), drink, visitedAtUtc, photos);
    }

    public bool Approve(Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        if (ModerationStatus == ModerationStatus.Approved)
            return false;

        ModeratedAt = DateTime.UtcNow;
        ModeratedBy = moderatorId;
        ModerationStatus = ModerationStatus.Approved;
        RejectedReason = null;
        DecisionSequence++;
        return true;
    }

    public bool Reject(string reason, Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reject reason is required.");

        if (reason.Length is < BusinessConstants.MinRejectReasonCommentLength or > BusinessConstants.MaxRejectReasonCommentLength)
            throw new DomainException(
                $"{nameof(reason)} must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");

        if (ModerationStatus == ModerationStatus.Rejected)
            return false;

        RejectedReason = reason.Trim();
        ModeratedAt = DateTime.UtcNow;
        ModeratedBy = moderatorId;
        ModerationStatus = ModerationStatus.Rejected;
        DecisionSequence++;
        return true;
    }

    public bool MoveToPending(Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        if (ModerationStatus == ModerationStatus.Pending)
            return false;

        RejectedReason = null;
        ModeratedAt = DateTime.UtcNow;
        ModeratedBy = moderatorId;
        ModerationStatus = ModerationStatus.Pending;
        DecisionSequence++;
        return true;
    }
}
