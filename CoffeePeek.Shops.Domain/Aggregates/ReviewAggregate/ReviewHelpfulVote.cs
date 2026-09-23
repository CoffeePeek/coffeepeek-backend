namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public sealed class ReviewHelpfulVote
{
    public Guid ReviewId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ReviewHelpfulVote()
    {
    }

    public ReviewHelpfulVote(Guid reviewId, Guid userId, DateTime createdAtUtc)
    {
        ReviewId = reviewId;
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
    }
}
