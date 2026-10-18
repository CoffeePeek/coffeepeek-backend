namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public sealed class UserBlock
{
    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private UserBlock() { }

    public UserBlock(Guid blockerId, Guid blockedId)
    {
        if (blockerId == Guid.Empty || blockedId == Guid.Empty || blockerId == blockedId)
            throw new ArgumentException("Two distinct nonempty user IDs are required.");
        BlockerId = blockerId;
        BlockedId = blockedId;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
