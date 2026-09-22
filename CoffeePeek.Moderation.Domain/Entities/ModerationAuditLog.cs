namespace CoffeePeek.Moderation.Domain.Entities;

public enum ModerationAuditEntityType
{
    Shop = 0,
    Review = 1,
    Roaster = 2,
    ShopChangeRequest = 3
}

public enum ModerationAuditAction
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class ModerationAuditLog
{
    public Guid Id { get; private set; }
    public ModerationAuditEntityType EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public string EntityName { get; private set; } = null!;
    public ModerationAuditAction Action { get; private set; }
    public Guid ModeratorUserId { get; private set; }
    public string? Comment { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private ModerationAuditLog() { }

    public static ModerationAuditLog Create(
        ModerationAuditEntityType entityType,
        Guid entityId,
        string entityName,
        ModerationAuditAction action,
        Guid moderatorUserId,
        string? comment)
    {
        return new ModerationAuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            Action = action,
            ModeratorUserId = moderatorUserId,
            Comment = comment,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
