namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed class CheckInHelpfulVote
{
    public Guid CheckInId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private CheckInHelpfulVote() { }
    public CheckInHelpfulVote(Guid checkInId, Guid userId, DateTime createdAtUtc)
    { CheckInId = checkInId; UserId = userId; CreatedAtUtc = createdAtUtc; }
}
