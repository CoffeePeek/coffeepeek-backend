using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Drinks;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public partial class ModerationReview
{
    public static ModerationReview CreateCheckInSnapshot(Guid checkInId, int contentRevision,
        Guid userId, Guid? shopId, string userName, string? text, int? place, int? service, int? coffee,
        DrinkSelection? drink, DateTime visitedAtUtc, List<PhotoMetadata> photos, HomePreparationSnapshot? home = null,
        string leaderboardGeoStatus = "MissingLocation", double? shopDistanceMeters = null,
        double? locationAccuracyMeters = null, DateTime? locationCapturedAtUtc = null)
    {
        if (checkInId == Guid.Empty || userId == Guid.Empty || shopId == Guid.Empty || contentRevision < 1)
            throw new DomainException("A check-in snapshot requires valid identity and revision.");
        if ((shopId == null) != (home != null) || home?.PreparedOn == default(DateOnly)
            || home?.BeanName?.Length > 250 || home?.TasteScore is < 1 or > 5
            || home != null && (place != null || service != null || coffee != null))
            throw new DomainException("A home snapshot requires preparation details and no shop rating.");
        if (text?.Trim().Length > BusinessConstants.MaxCheckInTextLength)
            throw new DomainException($"Check-in text must not exceed {BusinessConstants.MaxCheckInTextLength} characters.");
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

        var rating = place == null && service == null && coffee == null
            ? null : Rating.Create(place ?? 0, service ?? 0, coffee ?? 0);
        var review = new ModerationReview(checkInId, contentRevision, userId, shopId, userName, text?.Trim() ?? "",
            rating, drink, visitedAtUtc, photos);
        review.Home = home;
        review.LeaderboardGeoStatus = leaderboardGeoStatus;
        review.ShopDistanceMeters = shopDistanceMeters;
        review.LocationAccuracyMeters = locationAccuracyMeters;
        review.LocationCapturedAtUtc = locationCapturedAtUtc;
        if (review.Comment.Length == 0 && photos.Count == 0 && string.IsNullOrWhiteSpace(home?.BeanName)
            && string.IsNullOrWhiteSpace(drink?.CustomName))
        {
            review.ModerationStatus = ModerationStatus.Approved;
            review.ModeratedAt = review.CreatedAtUtc;
            review.DecisionSequence = 1;
        }
        return review;
    }

    public bool SetLeaderboardDecision(bool counted, string? reason, Guid administratorId)
    {
        if (administratorId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("A leaderboard decision requires an administrator and a reason of 1–1000 characters.");
        if (ShopId is null && counted)
            throw new DomainException("Home check-ins cannot participate in the leaderboard.");
        if (LeaderboardOverride == counted && LeaderboardReason == reason.Trim()) return false;
        LeaderboardOverride = counted;
        LeaderboardReason = reason.Trim();
        ModeratedBy = administratorId;
        ModeratedAt = DateTime.UtcNow;
        DecisionSequence++;
        return true;
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
