using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public sealed partial class Review
{
    public static Review Create(Guid shopId, Guid userId, string userName, string? header,
        string comment, int ratingPlace, int ratingService, int ratingCoffee, Guid moderationReviewId)
    {
        if (shopId == Guid.Empty)
            throw new DomainException($"{nameof(CoffeeShopId)} cannot be empty.");

        if (userId == Guid.Empty)
            throw new DomainException($"{nameof(UserId)} cannot be empty.");

        var normalizedHeader = string.IsNullOrWhiteSpace(header) ? null : header.Trim();
        if (normalizedHeader is { Length: < BusinessConstants.MinReviewHeaderLength or > BusinessConstants.MaxReviewHeaderLength })
            throw new DomainException(
                $"{nameof(header)} must be between {BusinessConstants.MinReviewHeaderLength} and {BusinessConstants.MaxReviewHeaderLength} characters.");

        if (string.IsNullOrWhiteSpace(comment))
            throw new DomainException("Review comment is required.");

        if (comment.Length is < BusinessConstants.MinReviewCommentLength or > BusinessConstants.MaxReviewCommentLength)
            throw new DomainException(
                $"{nameof(comment)} must be between {BusinessConstants.MinReviewCommentLength} and {BusinessConstants.MaxReviewCommentLength} characters.");

        var rating = Rating.Create(ratingPlace, ratingService, ratingCoffee);

        return new Review(shopId, userId, userName, normalizedHeader, comment.Trim(), rating, moderationReviewId);
    }

    public void UpdateContent(string? header, string comment, Rating rating)
    {
        var normalizedHeader = string.IsNullOrWhiteSpace(header) ? null : header.Trim();
        if (normalizedHeader is { Length: < BusinessConstants.MinReviewHeaderLength or > BusinessConstants.MaxReviewHeaderLength })
            throw new DomainException(
                $"{nameof(header)} must be between {BusinessConstants.MinReviewHeaderLength} and {BusinessConstants.MaxReviewHeaderLength} characters.");

        if (string.IsNullOrWhiteSpace(comment))
            throw new DomainException("Review comment is required.");

        if (comment.Length is < BusinessConstants.MinReviewCommentLength or > BusinessConstants.MaxReviewCommentLength)
            throw new DomainException(
                $"{nameof(comment)} must be between {BusinessConstants.MinReviewCommentLength} and {BusinessConstants.MaxReviewCommentLength} characters.");

        Header = normalizedHeader;
        Comment = comment.Trim();
        Rating = rating;
    }

    public void SoftDelete()
    {
        IsSoftDelete = true;
    }

    // ponytail: reusing IsSoftDelete as the "hide" mechanism — every public query already excludes
    // soft-deleted rows, so hiding an edited-but-unapproved review needs zero new filter points.
    // Ceiling: split into a dedicated IsHidden column only if delete and hide must ever diverge.
    public void Hide()
    {
        IsSoftDelete = true;
    }

    public void Restore()
    {
        IsSoftDelete = false;
    }

    public void LinkToModerationReview(Guid moderationReviewId)
    {
        if (ModerationReviewId.HasValue)
            return;

        ModerationReviewId = moderationReviewId;
    }
}
