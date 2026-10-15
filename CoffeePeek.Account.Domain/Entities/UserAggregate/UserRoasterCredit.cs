namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

/// <summary>One lifetime contribution per approved roaster, independent of message delivery IDs.</summary>
public sealed class UserRoasterCredit
{
    public Guid RoasterId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private UserRoasterCredit() { }

    public UserRoasterCredit(Guid roasterId, Guid userId)
    {
        if (roasterId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Nonempty roaster and user IDs are required.");
        RoasterId = roasterId;
        UserId = userId;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
