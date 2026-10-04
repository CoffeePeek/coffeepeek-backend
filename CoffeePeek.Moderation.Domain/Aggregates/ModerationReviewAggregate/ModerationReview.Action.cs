using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public partial class ModerationReview
{
    public static ModerationReview Create(Guid userId, Guid shopId, Guid? moderationShopId, string userName, string? header, string comment,
        int ratingPlace, int ratingService, int ratingCoffee, List<PhotoMetadata> photos)
    {
        if (shopId == Guid.Empty)
            throw new DomainException($"{nameof(shopId)} cannot be empty.");

        if (userId == Guid.Empty)
            throw new DomainException($"{nameof(userId)} cannot be empty.");

        var normalizedHeader = string.IsNullOrWhiteSpace(header) ? null : header.Trim();
        if (normalizedHeader != null &&
            normalizedHeader.Length is < BusinessConstants.MinReviewHeaderLength or > BusinessConstants.MaxReviewHeaderLength)
            throw new DomainException(
                $"{nameof(header)} must be between {BusinessConstants.MinReviewHeaderLength} and {BusinessConstants.MaxReviewHeaderLength} characters.");

        if (string.IsNullOrWhiteSpace(comment))
            throw new DomainException("Review comment is required.");

        if (comment.Length is < BusinessConstants.MinReviewCommentLength or > BusinessConstants.MaxReviewCommentLength)
            throw new DomainException(
                $"{nameof(comment)} must be between {BusinessConstants.MinReviewCommentLength} and {BusinessConstants.MaxReviewCommentLength} characters.");
        
        var rating = Rating.Create(ratingPlace,ratingService, ratingCoffee);

        return new Aggregates.ModerationReviewAggregate.ModerationReview(userId, shopId, moderationShopId, userName, normalizedHeader, comment.Trim(), rating, photos);
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
        return true;
    }
    
    public void Reject(string reason, Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reject reason is required.");

        if (reason.Length is < BusinessConstants.MinRejectReasonCommentLength or > BusinessConstants.MaxRejectReasonCommentLength)
            throw new DomainException(
                $"{nameof(reason)} must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");

        RejectedReason = reason;
        ModeratedAt = DateTime.UtcNow;
        ModeratedBy = moderatorId;
        ModerationStatus = ModerationStatus.Rejected;
    }
    
    public void Resubmit(Guid authorId)
    {
        if (authorId == Guid.Empty)
            throw new DomainException($"{nameof(authorId)} cannot be empty.");

        // Author-initiated re-moderation: do NOT stamp ModeratedBy/ModeratedAt (the actor
        // is the review author, not a moderator). Only clear the rejection and reset status.
        RejectedReason = null;
        ModerationStatus = ModerationStatus.Pending;
    }

    public void MoveToPending(Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        RejectedReason = null;
        ModeratedAt = DateTime.UtcNow;
        ModeratedBy = moderatorId;
        ModerationStatus = ModerationStatus.Pending;
    }

    public void ReplacePhotos(IEnumerable<PhotoMetadata> photos)
    {
        var replacement = photos.ToList();
        if (replacement.Count > 20 || replacement.Any(p => p.OwnerId != UserId || p.ModerationShopId is not null) ||
            replacement.Select(p => p.StorageKey).Distinct(StringComparer.Ordinal).Count() != replacement.Count)
            throw new DomainException("Review photos must be unique and belong to its author.");
        _reviewPhotos.Clear();
        _reviewPhotos.AddRange(replacement);
    }

    public void UpdateHeader(string? header)
    {
        var normalizedHeader = string.IsNullOrWhiteSpace(header) ? null : header.Trim();
        if (normalizedHeader == Header)
        {
            return;
        }
        
        if (normalizedHeader != null &&
            normalizedHeader.Length is < BusinessConstants.MinReviewHeaderLength or > BusinessConstants.MaxReviewHeaderLength)
        {
            throw new DomainException(
                $"{nameof(header)} header must be between {BusinessConstants.MinReviewHeaderLength} and {BusinessConstants.MaxReviewHeaderLength} characters.");
        }
        
        Header = normalizedHeader;
    }

    public void UpdateComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new DomainException("Review comment is required.");

        var normalizedComment = comment.Trim();
        if (normalizedComment.Length is < BusinessConstants.MinReviewCommentLength or > BusinessConstants.MaxReviewCommentLength)
        {
            throw new DomainException(
                $"{nameof(comment)} must be between {BusinessConstants.MinReviewCommentLength} and {BusinessConstants.MaxReviewCommentLength} characters.");
        }
        
        Comment = normalizedComment;
    }
}
