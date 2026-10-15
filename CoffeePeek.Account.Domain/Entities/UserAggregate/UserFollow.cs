namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public sealed class UserFollow
{
    public Guid FollowerId { get; private set; }
    public Guid FollowedId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private UserFollow() { }

    public UserFollow(Guid followerId, Guid followedId)
    {
        if (followerId == Guid.Empty || followedId == Guid.Empty || followerId == followedId)
            throw new ArgumentException("Two distinct nonempty user IDs are required.");
        FollowerId = followerId;
        FollowedId = followedId;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
