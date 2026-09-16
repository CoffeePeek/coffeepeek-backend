using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public sealed partial class Review
{
    public static Review Create(Guid shopId, Guid userId, string userName, string? header,
        string comment, int ratingPlace, int ratingService, int ratingCoffee)
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
        
        return new Review(shopId, userId, userName, normalizedHeader, comment.Trim(), rating);
    }

    public void SoftDelete()
    {
        IsSoftDelete = true;
    }
}
